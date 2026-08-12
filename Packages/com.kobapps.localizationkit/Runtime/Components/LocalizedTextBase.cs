using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// Shared behaviour for a component that puts a localized string into something.
    /// </summary>
    /// <remarks>
    /// Subclasses supply two things: where the text goes (<see cref="ApplyText"/>) and, optionally,
    /// what it currently says (<see cref="ReadText"/>, used once in the editor to seed the key).
    /// Everything else — registration, the key picker, the RTL flag, case folding — is here, so
    /// adding support for another text widget is a dozen lines.
    /// <para>
    /// Registration happens in <c>OnEnable</c> and is undone in <c>OnDisable</c>, which means a
    /// pooled object stops costing anything the moment it is deactivated, and a destroyed one
    /// cannot be resurrected by a language change.
    /// </para>
    /// </remarks>
    [ExecuteAlways]
    public abstract class LocalizedTextBase : MonoBehaviour, ILocalizedObject
    {
        [SerializeField, LocalizationKey] private string m_Key;
        [SerializeField] private LocalizedTextCase m_Case = LocalizedTextCase.AsAuthored;
        [SerializeField] private bool m_ApplyRightToLeftAlignment;

        private LocalizationSubscription m_Subscription;
        private LocalizationHandle m_Handle;

        /// <summary>
        /// The catalog key this shows. Assigning re-resolves and reapplies immediately, so changing
        /// it from code needs no follow-up call.
        /// </summary>
        public string Key
        {
            get => m_Key;
            set
            {
                if (string.Equals(m_Key, value, System.StringComparison.Ordinal)) return;

                m_Key = value;
                m_Handle = Localization.Resolve(m_Key);
                ApplyLocalization();
            }
        }

        /// <summary>Case transform applied after lookup. Turkish-safe: uses invariant culture.</summary>
        public LocalizedTextCase Case
        {
            get => m_Case;
            set
            {
                m_Case = value;
                ApplyLocalization();
            }
        }

        /// <summary>
        /// Whether a right-to-left language flips this component's alignment. Off by default —
        /// a layout already mirrored by its parent would be flipped twice.
        /// </summary>
        public bool ApplyRightToLeftAlignment
        {
            get => m_ApplyRightToLeftAlignment;
            set => m_ApplyRightToLeftAlignment = value;
        }

        /// <summary>Puts resolved text into the target widget.</summary>
        protected abstract void ApplyText(string value);

        /// <summary>
        /// Current text of the target widget, for the editor's "adopt this string as a key" flow.
        /// Returning null opts out.
        /// </summary>
        protected virtual string ReadText() => null;

        /// <summary>Called when the active language reads in a different direction.</summary>
        protected virtual void ApplyDirection(bool rightToLeft) { }

        /// <inheritdoc />
        public virtual void ApplyLocalization()
        {
            if (string.IsNullOrEmpty(m_Key)) return;

            var value = Localization.GetValue(ref m_Handle);

            switch (m_Case)
            {
                case LocalizedTextCase.Upper:
                    value = value.ToUpperInvariant();
                    break;
                case LocalizedTextCase.Lower:
                    value = value.ToLowerInvariant();
                    break;
            }

            ApplyText(value);

            if (m_ApplyRightToLeftAlignment)
                ApplyDirection(Localization.IsRightToLeft);
        }

        /// <summary>
        /// Reads the widget's authored text. Public so the editor can offer to turn a hand-typed
        /// label into a catalog entry without reflecting over protected members.
        /// </summary>
        public string ReadCurrentText() => ReadText();

        protected virtual void OnEnable()
        {
            m_Handle = Localization.Resolve(m_Key);
            m_Subscription = LocalizationBinder.Register(this);
        }

        protected virtual void OnDisable()
        {
            LocalizationBinder.Unregister(ref m_Subscription);
        }

        /// <summary>
        /// Keeps the scene view honest while the key is being edited. Editor-only: the whole body
        /// compiles away in a player, so this costs a build nothing.
        /// </summary>
        protected virtual void OnValidate()
        {
#if UNITY_EDITOR
            if (!isActiveAndEnabled) return;

            m_Handle = Localization.Resolve(m_Key);
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this == null) return;
                ApplyLocalization();
            };
#endif
        }
    }

    /// <summary>Case transform a localized text component applies after lookup.</summary>
    public enum LocalizedTextCase
    {
        /// <summary>Whatever the catalog says.</summary>
        AsAuthored = 0,

        /// <summary>Upper case, invariant culture.</summary>
        Upper = 1,

        /// <summary>Lower case, invariant culture.</summary>
        Lower = 2,
    }
}
