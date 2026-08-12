using UnityEngine;
using UnityEngine.UI;

namespace LocalizationKit.Samples
{
    /// <summary>
    /// The attribute-driven half of the sample: four fields that are never assigned by hand.
    /// </summary>
    /// <remarks>
    /// The class is <c>partial</c> and declares no <c>OnEnable</c>, so the generator supplies the
    /// lifecycle. By the time <see cref="OnLocalizationApplied"/> runs, every field below already
    /// holds text in the active language — on the first frame and after every change.
    /// <para>
    /// Compare with <see cref="LocalizedText"/> on the sibling objects in the scene: same result,
    /// no script. Use the attribute when the string goes somewhere a component cannot reach.
    /// </para>
    /// </remarks>
    [AddComponentMenu("LocalizationKit/Samples/Showcase Panel")]
    public partial class ShowcasePanel : MonoBehaviour
    {
        [Localized("Default/AppName")] private string m_AppName;
        [Localized("Store/BuyButton")] private string m_Buy;
        [Localized("Popups/Quit/Title")] private string m_QuitTitle;
        [Localized("Tutorials/Step1")] private string m_TutorialStep;

        [SerializeField] private Text m_Output;

        // A key chosen in the inspector rather than compiled in: the picker draws a searchable
        // dropdown over the catalog for this field.
        [SerializeField, LocalizationKey] private string m_ExtraKey;

        /// <summary>
        /// Runs after every refresh, including the first. This is the hook to rebuild anything
        /// derived from the text — a layout, a cached width, a formatted composite.
        /// </summary>
        partial void OnLocalizationApplied()
        {
            if (m_Output == null) return;

            var extra = string.IsNullOrEmpty(m_ExtraKey) ? "—" : Localization.Get(m_ExtraKey);

            m_Output.text =
                $"[Localized] fields, filled by the generator:\n\n"
                + $"  Default/AppName      →  {m_AppName}\n"
                + $"  Store/BuyButton      →  {m_Buy}\n"
                + $"  Popups/Quit/Title    →  {m_QuitTitle}\n"
                + $"  Tutorials/Step1      →  {m_TutorialStep}\n\n"
                + $"[LocalizationKey] field, chosen in the inspector:\n\n"
                + $"  {(string.IsNullOrEmpty(m_ExtraKey) ? "(none selected)" : m_ExtraKey)}  →  {extra}";
        }
    }
}
