namespace LocalizationKit
{
    /// <summary>
    /// Something that has text to refresh when the language changes.
    /// </summary>
    /// <remarks>
    /// Implemented for you by the source generator on any class with a <see cref="LocalizedAttribute"/>
    /// field, and by the shipped text components. Implement it directly only when you want to
    /// hand-roll the binding.
    /// </remarks>
    public interface ILocalizedObject
    {
        /// <summary>
        /// Pull current text out of <see cref="Localization"/> and apply it.
        /// Called once on registration and again on every language change.
        /// </summary>
        void ApplyLocalization();
    }

    /// <summary>
    /// A registration with <see cref="LocalizationBinder"/>. Hold it; pass it back to unregister.
    /// </summary>
    /// <remarks>
    /// The slot is what makes removal O(1) — without it, unregistering means scanning the whole
    /// list, and a scene tear-down would be quadratic in the number of localized objects.
    /// </remarks>
    public struct LocalizationSubscription
    {
        internal int m_Slot;

        /// <summary>True while this subscription refers to a live registration.</summary>
        public bool IsActive => m_Slot > 0;

        internal LocalizationSubscription(int slot)
        {
            m_Slot = slot;
        }
    }
}
