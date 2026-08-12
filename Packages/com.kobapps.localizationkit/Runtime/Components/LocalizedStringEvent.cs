using UnityEngine;
using UnityEngine.Events;

namespace LocalizationKit
{
    /// <summary>
    /// Routes a localized string into a <see cref="UnityEvent"/>, for targets the kit ships no
    /// component for — a custom text renderer, a tooltip, a speech bubble, an analytics label.
    /// </summary>
    /// <remarks>
    /// The escape hatch that keeps the kit from needing a component per widget type. It is wired
    /// in the inspector, so a designer can localize something the kit has never heard of without a
    /// programmer writing a subclass.
    /// </remarks>
    [AddComponentMenu("LocalizationKit/Localized String Event")]
    public sealed class LocalizedStringEvent : MonoBehaviour, ILocalizedObject
    {
        [SerializeField, LocalizationKey] private string m_Key;
        [SerializeField] private UnityEvent<string> m_OnLocalized = new UnityEvent<string>();

        private LocalizationSubscription m_Subscription;
        private LocalizationHandle m_Handle;

        /// <summary>The catalog key this raises text for.</summary>
        public string Key
        {
            get => m_Key;
            set
            {
                m_Key = value;
                m_Handle = Localization.Resolve(m_Key);
                ApplyLocalization();
            }
        }

        /// <summary>Raised with current text on enable and on every language change.</summary>
        public UnityEvent<string> OnLocalized => m_OnLocalized;

        /// <inheritdoc />
        public void ApplyLocalization()
        {
            if (string.IsNullOrEmpty(m_Key)) return;

            m_OnLocalized.Invoke(Localization.GetValue(ref m_Handle));
        }

        private void OnEnable()
        {
            m_Handle = Localization.Resolve(m_Key);
            m_Subscription = LocalizationBinder.Register(this);
        }

        private void OnDisable()
        {
            LocalizationBinder.Unregister(ref m_Subscription);
        }
    }
}
