using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace LocalizationKit
{
    /// <summary>What an HTTP call came back with.</summary>
    public sealed class LocalizationWebResponse
    {
        /// <summary>True for a completed request with a 2xx status.</summary>
        public bool Success;

        /// <summary>HTTP status, or 0 when the request never reached a server.</summary>
        public long StatusCode;

        /// <summary>The body, as text. Empty rather than null on failure.</summary>
        public string Text = string.Empty;

        /// <summary>
        /// The body, as bytes. Null on failure.
        /// </summary>
        /// <remarks>
        /// Not everything a provider fetches is text. A workbook export is a zip, and reading the
        /// tab names out of one is the difference between typing eight of them by hand and pressing
        /// a button.
        /// </remarks>
        public byte[] Data;

        /// <summary>Why it failed, when it did — already carrying the URL it was for.</summary>
        public string Error;
    }

    /// <summary>
    /// The HTTP a provider needs, working the same from an editor window, from a player, and from
    /// a build machine.
    /// </summary>
    /// <remarks>
    /// This exists because of one awkward fact: <c>UnityWebRequest</c> is driven by the player
    /// loop, and there are two situations a localization fetch has to work in where that loop is
    /// not running.
    /// <list type="bullet">
    /// <item><b>The editor, outside play mode.</b> A provider written the obvious way works
    /// perfectly in play mode and hangs forever when the same button is pressed in an editor
    /// window — which is exactly where a translator presses it. Requests are therefore polled from
    /// <c>EditorApplication.update</c>, and from a hidden <c>DontDestroyOnLoad</c> behaviour in
    /// play mode.</item>
    /// <item><b>A build machine.</b> <c>-batchmode -executeMethod</c> runs a method to completion
    /// with no update loop underneath it at all, so <em>nothing</em> would ever pump the request
    /// and no amount of waiting would help. In batch mode the call is therefore made with
    /// <c>System.Net</c> and completes synchronously, before the calling method returns.</item>
    /// </list>
    /// Providers see none of this. They call <see cref="Get"/> or <see cref="Post"/> and get a
    /// callback exactly once; whether it arrives on a later frame or before the call returns is
    /// this class's problem.
    /// <para>
    /// A failure comes back as a response with <see cref="LocalizationWebResponse.Error"/> set,
    /// never as an exception, because a fetch failing is an ordinary event — someone is offline, or
    /// the sheet is not shared — and a stack trace is the wrong way to say so.
    /// </para>
    /// </remarks>
    public static class LocalizationWeb
    {
        /// <summary>Seconds a request is given before it is abandoned.</summary>
        public const int DefaultTimeoutSeconds = 30;

        /// <summary>Content type for a form post — what Apps Script and most simple endpoints expect.</summary>
        public const string FormContentType = "application/x-www-form-urlencoded";

        private sealed class Pending
        {
            public UnityWebRequest Request;
            public UnityWebRequestAsyncOperation Operation;
            public Action<LocalizationWebResponse> OnCompleted;
        }

        private static readonly List<Pending> s_Pending = new List<Pending>();

#if UNITY_EDITOR
        private static bool s_EditorHooked;
#endif

        /// <summary>
        /// Forces every call to complete before it returns, rather than on a later frame.
        /// </summary>
        /// <remarks>
        /// On by default in batch mode, where nothing would pump an asynchronous request. Set it
        /// yourself for an editor script that has to finish its work inside one method — a
        /// migration, a test, a CI step invoked some way other than <c>-batchmode</c>.
        /// <para>
        /// It has no effect in a player: a game must not stall its main thread on the network, and
        /// on WebGL a blocking HTTP call is not possible at all.
        /// </para>
        /// </remarks>
        public static bool Blocking { get; set; } = Application.isBatchMode;

        /// <summary>True while any request started here is still in flight.</summary>
        public static bool HasPendingRequests => s_Pending.Count > 0;

        /// <summary>GETs a URL and hands back the body as text.</summary>
        public static void Get(
            string url,
            Action<LocalizationWebResponse> onCompleted,
            IReadOnlyDictionary<string, string> headers = null,
            int timeoutSeconds = DefaultTimeoutSeconds) =>
            Dispatch(UnityWebRequest.kHttpVerbGET, url, null, null, headers, timeoutSeconds, onCompleted);

        /// <summary>POSTs a body and hands back the response as text.</summary>
        /// <param name="contentType">
        /// Sent as-is. <see cref="FormContentType"/> is worth knowing about: a Google Apps Script
        /// web app answers with a redirect, and a request carrying a JSON content type is refused
        /// when it follows one — a long afternoon to discover on your own.
        /// </param>
        public static void Post(
            string url,
            string body,
            string contentType,
            Action<LocalizationWebResponse> onCompleted,
            IReadOnlyDictionary<string, string> headers = null,
            int timeoutSeconds = DefaultTimeoutSeconds) =>
            Dispatch(UnityWebRequest.kHttpVerbPOST, url, body, contentType, headers, timeoutSeconds, onCompleted);

        /// <summary>
        /// Runs the pump until nothing is in flight, or the timeout elapses. Returns false on
        /// timeout.
        /// </summary>
        /// <remarks>
        /// Only useful in the editor, and only outside play mode: it spins the same update the
        /// editor would have spun, which is a thing a game must never do to its own main thread.
        /// In batch mode there is nothing pending to wait for — see <see cref="Blocking"/> — so
        /// this returns immediately.
        /// </remarks>
        public static bool WaitForPendingRequests(float timeoutSeconds = 120f)
        {
            var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);

            while (s_Pending.Count > 0)
            {
                Tick();

                if (s_Pending.Count == 0) break;

                if (DateTime.UtcNow > deadline)
                {
                    AbortPending("Timed out waiting for a localization request.");
                    return false;
                }

                System.Threading.Thread.Sleep(16);
            }

            return true;
        }

        // ---------------------------------------------------------------- dispatch

        private static void Dispatch(
            string method,
            string url,
            string body,
            string contentType,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSeconds,
            Action<LocalizationWebResponse> onCompleted)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                Invoke(onCompleted, new LocalizationWebResponse { Error = "No URL." });
                return;
            }

            timeoutSeconds = Mathf.Max(1, timeoutSeconds);

#if UNITY_EDITOR
            if (Blocking && !Application.isPlaying)
            {
                Invoke(onCompleted, SendSynchronously(method, url, body, contentType, headers, timeoutSeconds));
                return;
            }
#endif

            var request = BuildRequest(method, url, body, contentType);

            if (headers != null)
            {
                foreach (var header in headers)
                    request.SetRequestHeader(header.Key, header.Value);
            }

            request.timeout = timeoutSeconds;

            // Google, and most CDNs, answer a fetch with a redirect to a signed URL.
            request.redirectLimit = Mathf.Max(request.redirectLimit, 8);

            s_Pending.Add(new Pending
            {
                Request = request,
                Operation = request.SendWebRequest(),
                OnCompleted = onCompleted
            });

            Hook();
        }

        private static UnityWebRequest BuildRequest(string method, string url, string body, string contentType)
        {
            if (method != UnityWebRequest.kHttpVerbPOST) return UnityWebRequest.Get(url);

            var request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(body ?? string.Empty)),
                downloadHandler = new DownloadHandlerBuffer()
            };

            request.uploadHandler.contentType = string.IsNullOrEmpty(contentType) ? FormContentType : contentType;

            return request;
        }

#if UNITY_EDITOR
        /// <summary>
        /// The batch-mode path: <c>System.Net</c>, which owes nothing to Unity's update loop.
        /// </summary>
        private static LocalizationWebResponse SendSynchronously(
            string method,
            string url,
            string body,
            string contentType,
            IReadOnlyDictionary<string, string> headers,
            int timeoutSeconds)
        {
            var response = new LocalizationWebResponse();

            try
            {
                var request = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);

                request.Method = method;
                request.Timeout = timeoutSeconds * 1000;
                request.ReadWriteTimeout = timeoutSeconds * 1000;
                request.AllowAutoRedirect = true;
                request.UserAgent = "LocalizationKit";

                if (headers != null)
                {
                    foreach (var header in headers)
                        request.Headers[header.Key] = header.Value;
                }

                if (method == UnityWebRequest.kHttpVerbPOST)
                {
                    var payload = System.Text.Encoding.UTF8.GetBytes(body ?? string.Empty);

                    request.ContentType = string.IsNullOrEmpty(contentType) ? FormContentType : contentType;
                    request.ContentLength = payload.Length;

                    using var stream = request.GetRequestStream();
                    stream.Write(payload, 0, payload.Length);
                }

                using var webResponse = (System.Net.HttpWebResponse)request.GetResponse();
                using var responseStream = webResponse.GetResponseStream() ?? System.IO.Stream.Null;
                using var buffer = new System.IO.MemoryStream();

                responseStream.CopyTo(buffer);

                response.StatusCode = (long)webResponse.StatusCode;
                response.Data = buffer.ToArray();

                // Decoded here rather than through a StreamReader so the bytes survive too: a
                // caller after a zip needs them, and reading the stream twice is not an option.
                response.Text = System.Text.Encoding.UTF8.GetString(response.Data);
                response.Success = response.StatusCode >= 200 && response.StatusCode < 300;

                if (!response.Success)
                    response.Error = $"HTTP {response.StatusCode} for {url}: {Trim(response.Text)}";
            }
            catch (System.Net.WebException exception)
            {
                response.Error = $"{exception.Message} for {url}";

                // The body of a refusal usually says why, and that sentence is worth far more in a
                // CI log than "The remote server returned an error: (400) Bad Request".
                try
                {
                    if (exception.Response != null)
                    {
                        using var reader = new System.IO.StreamReader(
                            exception.Response.GetResponseStream() ?? System.IO.Stream.Null);

                        var detail = reader.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(detail)) response.Error += $": {Trim(detail)}";
                    }
                }
                catch
                {
                    // Nothing more to learn; the message above stands on its own.
                }
            }
            catch (Exception exception)
            {
                response.Error = $"{exception.Message} for {url}";
            }

            return response;
        }
#endif

        // ---------------------------------------------------------------- pumping

        private static void Hook()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                if (s_EditorHooked) return;

                UnityEditor.EditorApplication.update += Tick;
                s_EditorHooked = true;
                return;
            }
#endif
            Pump.Ensure();
        }

        private static void Tick()
        {
            for (var i = s_Pending.Count - 1; i >= 0; i--)
            {
                var pending = s_Pending[i];

                // A domain reload or an Abort elsewhere can leave a dead entry behind; drop it
                // rather than polling a disposed request forever.
                if (pending.Request == null)
                {
                    s_Pending.RemoveAt(i);
                    continue;
                }

                if (!pending.Operation.isDone) continue;

                s_Pending.RemoveAt(i);
                Complete(pending);
            }

            if (s_Pending.Count == 0) Unhook();
        }

        private static void Complete(Pending pending)
        {
            var request = pending.Request;
            var response = new LocalizationWebResponse { StatusCode = request.responseCode };

            try
            {
                if (request.result == UnityWebRequest.Result.Success)
                {
                    response.Success = true;

                    if (request.downloadHandler != null)
                    {
                        response.Data = request.downloadHandler.data;
                        response.Text = request.downloadHandler.text;
                    }
                }
                else
                {
                    response.Error = $"{request.error} ({request.responseCode}) for {request.url}";

                    var body = request.downloadHandler != null ? request.downloadHandler.text : null;
                    if (!string.IsNullOrWhiteSpace(body)) response.Error += $": {Trim(body)}";
                }
            }
            finally
            {
                request.Dispose();
            }

            Invoke(pending.OnCompleted, response);
        }

        private static void AbortPending(string reason)
        {
            for (var i = s_Pending.Count - 1; i >= 0; i--)
            {
                var pending = s_Pending[i];
                s_Pending.RemoveAt(i);

                pending.Request?.Abort();
                pending.Request?.Dispose();

                Invoke(pending.OnCompleted, new LocalizationWebResponse { Error = reason });
            }

            Unhook();
        }

        private static void Invoke(Action<LocalizationWebResponse> callback, LocalizationWebResponse response)
        {
            try
            {
                callback?.Invoke(response);
            }
            catch (Exception exception)
            {
                // A throwing callback must not take the pump down with it — every other request in
                // flight would stall.
                Debug.LogException(exception);
            }
        }

        private static void Unhook()
        {
#if UNITY_EDITOR
            if (s_EditorHooked)
            {
                UnityEditor.EditorApplication.update -= Tick;
                s_EditorHooked = false;
            }
#endif
            Pump.Release();
        }

        private static string Trim(string body)
        {
            body = body.Trim();
            return body.Length <= 300 ? body : body.Substring(0, 300) + "…";
        }

        /// <summary>Drives <see cref="Tick"/> in play mode, where there is no editor update loop.</summary>
        private sealed class Pump : MonoBehaviour
        {
            private static Pump s_Instance;

            internal static void Ensure()
            {
                if (s_Instance != null) return;

                var host = new GameObject("LocalizationKit Web") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(host);

                s_Instance = host.AddComponent<Pump>();
            }

            internal static void Release()
            {
                if (s_Instance == null) return;

                var host = s_Instance.gameObject;
                s_Instance = null;

                if (Application.isPlaying) Destroy(host);
                else DestroyImmediate(host);
            }

            private void Update() => Tick();
        }
    }
}
