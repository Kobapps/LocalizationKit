using System.Runtime.CompilerServices;

namespace LocalizationKit
{
    /// <summary>
    /// A key that has already been looked up. Reading through one skips the dictionary entirely.
    /// </summary>
    /// <remarks>
    /// Resolve a handle once — in <c>Awake</c>, in a field initialiser, wherever the cost does not
    /// matter — then read it as often as you like. <see cref="Localization.GetValue(ref LocalizationHandle)"/>
    /// turns into a bounds check and an array index.
    /// <para>
    /// A handle keeps the key it came from and the table version it was resolved against. If the
    /// table is rebuilt underneath it, the version stops matching and the next read re-resolves
    /// rather than reading a stale row. That is the difference between a handle and a raw index,
    /// and it is why handles are safe to hold across a remote catalog refresh.
    /// </para>
    /// This is a struct with no reference to anything but the key string, so holding thousands of
    /// them costs nothing beyond the array they sit in.
    /// </remarks>
    public struct LocalizationHandle
    {
        internal string m_Key;
        internal int m_Index;
        internal int m_Version;

        /// <summary>The full key this handle was resolved from.</summary>
        public string Key => m_Key;

        /// <summary>True when this handle came from a real key that the table carried.</summary>
        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => m_Index >= 0;
        }

        internal LocalizationHandle(string key, int index, int version)
        {
            m_Key = key;
            m_Index = index;
            m_Version = version;
        }

        /// <summary>A handle that resolves to nothing.</summary>
        public static LocalizationHandle None => new LocalizationHandle(null, -1, 0);

        public override string ToString() => m_Key ?? "<none>";
    }
}
