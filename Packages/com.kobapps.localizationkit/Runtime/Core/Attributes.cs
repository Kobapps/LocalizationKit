using System;
using UnityEngine;

namespace LocalizationKit
{
    /// <summary>
    /// Binds a string field to a localization key. The source generator fills the field in and
    /// keeps it in step with the active language.
    /// </summary>
    /// <remarks>
    /// <code>
    /// public partial class ShopPanel : MonoBehaviour
    /// {
    ///     [Localized("Store/BuyButton")] private string m_BuyLabel;
    /// }
    /// </code>
    /// The generator emits a partial half of the class implementing <see cref="ILocalizedObject"/>,
    /// plus <c>EnableLocalization</c> / <c>DisableLocalization</c>. For a <see cref="MonoBehaviour"/>
    /// it also emits <c>OnEnable</c> and <c>OnDisable</c> that call them — <b>unless the class
    /// already declares one</b>, in which case it says so at compile time (LK003) and you call the
    /// two methods yourself. It cannot merge with a method you wrote, and silently not binding
    /// would be far worse than a warning.
    /// <para>
    /// The class must be <c>partial</c>. Fields must be instance, writable and of type
    /// <see cref="string"/>.
    /// </para>
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
    public sealed class LocalizedAttribute : Attribute
    {
        /// <summary>The full <c>Category/Key</c> this field takes its text from.</summary>
        public string Key { get; }

        public LocalizedAttribute(string key)
        {
            Key = key;
        }
    }

    /// <summary>
    /// Marks a string field as holding a localization key, which turns its inspector row into a
    /// searchable picker over the catalog instead of a free-text box.
    /// </summary>
    /// <remarks>
    /// <code>
    /// [SerializeField, LocalizationKey] private string m_TitleKey;
    /// [SerializeField, LocalizationKey("Popups")] private string m_BodyKey;  // scoped to one category
    /// </code>
    /// This is a <see cref="PropertyAttribute"/> rather than a plain attribute so Unity routes the
    /// field to the kit's drawer. It has no runtime behaviour of its own — pair it with
    /// <see cref="Localization.Get(string)"/> or a <c>LocalizedText</c> component.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class LocalizationKeyAttribute : PropertyAttribute
    {
        /// <summary>When set, the picker only offers keys from this category.</summary>
        public string Category { get; }

        /// <summary>Whether the picker lets you type a key the catalog does not carry yet.</summary>
        public bool AllowMissing { get; }

        public LocalizationKeyAttribute()
        {
            Category = null;
            AllowMissing = false;
        }

        public LocalizationKeyAttribute(string category, bool allowMissing = false)
        {
            Category = category;
            AllowMissing = allowMissing;
        }
    }
}
