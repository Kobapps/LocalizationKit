using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace LocalizationKit.Editor.Skill
{
    /// <summary>
    /// Installs the bundled "localizationkit" Claude skill into the project's
    /// <c>.claude/skills/</c> folder, so AI assistants working in this project get accurate
    /// guidance (how keys compose, which of the four binding styles to use, the settings asset
    /// that fails silently, how to bulk-localize existing text, how to verify via the Unity MCP).
    /// </summary>
    /// <remarks>
    /// The skill source ships inside the package under <c>Editor/Skill/SkillTemplate~/</c>. The
    /// <c>~</c> suffix hides it from Unity's asset import, but the files still travel with the
    /// package (UPM includes <c>~</c> folders) and stay on disk, so they can be resolved and
    /// copied at install time. The destination is the consuming project's root
    /// <c>.claude/skills/localizationkit/</c> — where Claude Code and the Agent SDK look for
    /// project-scoped skills.
    /// </remarks>
    internal static class LocalizationKitSkillInstaller
    {
        internal const string SkillName = "localizationkit";

        /// <summary>Absolute path to the project root (one level above <c>Assets/</c>).</summary>
        private static string ProjectRoot => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        /// <summary>Where the skill is installed to (project-scoped).</summary>
        internal static string DestinationDir =>
            Path.Combine(ProjectRoot, ".claude", "skills", SkillName);

        /// <summary>True when the skill's SKILL.md already exists at the destination.</summary>
        internal static bool IsInstalled() => File.Exists(Path.Combine(DestinationDir, "SKILL.md"));

        /// <summary>
        /// Resolves the on-disk folder holding the shipped skill template. Works whether the
        /// package is embedded in <c>Assets/</c>, embedded under <c>Packages/</c>, or resolved
        /// from the Package Cache — <see cref="FileUtil.GetPhysicalPath"/> maps the asset path to
        /// its real location.
        /// </summary>
        internal static string SourceDir()
        {
            var guids = AssetDatabase.FindAssets("LocalizationKitSkillInstaller t:MonoScript");

            for (var i = 0; i < guids.Length; i++)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!assetPath.EndsWith("LocalizationKitSkillInstaller.cs", StringComparison.Ordinal))
                    continue;

                var scriptDir = Path.GetDirectoryName(ToPhysicalPath(assetPath));
                if (string.IsNullOrEmpty(scriptDir)) continue;

                var candidate = Path.Combine(scriptDir, "SkillTemplate~", SkillName);
                if (Directory.Exists(candidate)) return candidate;
            }

            return null;
        }

        private static string ToPhysicalPath(string assetPath)
        {
            // GetPhysicalPath resolves Packages/PackageCache paths; fall back to a plain
            // project-relative resolve for older editors and embedded-in-Assets layouts.
            try
            {
                var physical = FileUtil.GetPhysicalPath(assetPath);
                if (!string.IsNullOrEmpty(physical)) return Path.GetFullPath(physical);
            }
            catch
            {
                // Not available in this editor version — fall through.
            }

            return Path.GetFullPath(assetPath);
        }

        /// <summary>
        /// Copies the skill template into <see cref="DestinationDir"/>. Returns the destination
        /// path. Throws with a readable message the caller can surface in a dialog.
        /// </summary>
        internal static string Install(bool overwrite)
        {
            var source = SourceDir();
            if (source == null)
            {
                throw new FileNotFoundException(
                    "Could not locate the bundled skill template (Editor/Skill/SkillTemplate~/). "
                    + "Reimport the LocalizationKit package.");
            }

            var destination = DestinationDir;
            if (Directory.Exists(destination) && !overwrite)
                return destination;

            CopyDirectory(source, destination);
            return destination;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source))
            {
                // Skip Unity meta files if any ever land in the template folder.
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }

            foreach (var directory in Directory.GetDirectories(source))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }

        // ---------------------------------------------------------------- menu

        [MenuItem("Tools/LocalizationKit/Install AI Skill", priority = 200)]
        private static void InstallInteractive()
        {
            var alreadyThere = IsInstalled();

            var proceed = EditorUtility.DisplayDialog(
                alreadyThere ? "Update AI skill?" : "Install AI skill?",
                $"This writes the 'localizationkit' skill to:\n\n{DestinationDir}\n\n"
                + "AI assistants working in this project then get accurate guidance on adding "
                + "languages and keys, binding text, and the failure modes that produce no error.\n\n"
                + (alreadyThere ? "A skill is already installed there and will be overwritten." : string.Empty),
                alreadyThere ? "Update" : "Install",
                "Cancel");

            if (!proceed) return;

            try
            {
                var destination = Install(overwrite: true);

                Debug.Log($"[LocalizationKit] AI skill installed to {destination}");
                EditorUtility.RevealInFinder(Path.Combine(destination, "SKILL.md"));
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Could not install the skill", exception.Message, "OK");
            }
        }
    }
}
