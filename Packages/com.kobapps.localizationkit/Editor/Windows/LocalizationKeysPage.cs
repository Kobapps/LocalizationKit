using System;
using System.Collections.Generic;
using EditorCoreKit.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LocalizationKit.Editor
{
    /// <summary>
    /// Browse, search, add and translate keys: categories on the left, a key/term table in the
    /// middle, and the selected key's text per language below it.
    /// </summary>
    /// <remarks>
    /// The key list is a <see cref="KUIVirtualList{T}"/> because a shipped game's catalog runs to
    /// thousands of entries and building a row per key would make the window unusable at exactly
    /// the size where the window matters most.
    /// <para>
    /// Rows are recycled, so <see cref="BindRow"/> resets every field it can set. A custom row
    /// element is used rather than <c>KUIListRow</c> for the same reason: <c>WithBadge</c> appends,
    /// and appending during a rebind would grow badges without limit as the user scrolls.
    /// </para>
    /// <para>
    /// A row is one line — key, then the default language's text — and every column is clipped to
    /// its share of the width. Rows in a virtual list are absolutely positioned at a fixed height,
    /// so anything that wraps does not make its row taller, it spills over the row beneath it. A
    /// translation is arbitrary text, so the columns must never wrap.
    /// </para>
    /// </remarks>
    internal sealed class LocalizationKeysPage
    {
        /// <summary>
        /// Row height. Must stay at or above the stylesheet's 26px minimum for a list item, or the
        /// rows render taller than the list positions them and every row overlaps the next.
        /// </summary>
        private const float k_RowHeight = 26f;

        /// <summary>Width of the trailing status column, in the header and in every row.</summary>
        private const float k_StatusWidth = 46f;

        /// <summary>How the key and term columns divide the width left over after the status column.</summary>
        private const float k_KeyFlex = 1f;
        private const float k_TermFlex = 1.4f;

        /// <summary>Width of the label column in the detail pane, shared by every row in it.</summary>
        private const float k_DetailLabelWidth = 110f;

        private readonly LocalizationKitWindow m_Window;

        private readonly List<EntryRef> m_Visible = new List<EntryRef>();

        /// <summary>Category paths whose children are folded away. Survives a page rebuild.</summary>
        private readonly HashSet<string> m_Collapsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private string m_Category;              // null = all categories; otherwise a path and its subtree
        private string m_Search = string.Empty;
        private bool m_OnlyMissing;
        private string m_SelectedKey;
        private string m_PendingSelectKey;

        private KUIVirtualList<EntryRef> m_List;
        private VisualElement m_Detail;
        private Label m_Count;

        internal LocalizationKeysPage(LocalizationKitWindow window)
        {
            m_Window = window;
        }

        /// <summary>One entry, plus the category it came from — the pair every operation needs.</summary>
        private readonly struct EntryRef
        {
            internal readonly LocalizationCategory Category;
            internal readonly LocalizationEntry Entry;

            internal string FullKey => LocalizationKeys.Compose(Category.Name, Entry.Key);

            internal EntryRef(LocalizationCategory category, LocalizationEntry entry)
            {
                Category = category;
                Entry = entry;
            }
        }

        /// <summary>Selects a key, switching category filters if needed. Applied on the next build.</summary>
        internal void SelectKey(string fullKey)
        {
            m_PendingSelectKey = fullKey;
            m_Category = null;
            m_Search = string.Empty;
        }

        internal VisualElement Build()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return m_Window.BuildNoCatalogState();

            if (catalog.Languages.Count == 0)
            {
                return KUILayout.Page(new KUIEmptyState(
                    "No languages yet",
                    "Keys need at least one language to hold text.",
                    "Add a Language",
                    () => m_Window.ShowPage(1),
                    "🌐"));
            }

            if (!string.IsNullOrEmpty(m_PendingSelectKey))
            {
                m_SelectedKey = m_PendingSelectKey;
                m_PendingSelectKey = null;
            }

            var split = new KUISplitView(200f, false, "LocalizationKit.KeysSplit");
            split.style.flexGrow = 1;

            split.First.Add(BuildCategoryList(catalog));
            split.Second.Add(BuildKeyPane(catalog));

            return split;
        }

        // ---------------------------------------------------------------- categories

        private VisualElement BuildCategoryList(LocalizationCatalog catalog)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.minHeight = 0;
            column.style.marginRight = 4f;

            var tree = BuildCategoryTree(catalog);

            var toolbar = new KUIToolbar();
            toolbar.With(KUIText.SectionTitle("Categories"));
            toolbar.PushRight();

            // Only worth the buttons when there is something nested to fold.
            if (HasNesting(tree))
            {
                toolbar.With(KUIButton.Icon(KUIIcons.ArrowDown, () => SetAllCollapsed(tree, false), "Expand all"));
                toolbar.With(KUIButton.Icon(KUIIcons.ArrowRight, () => SetAllCollapsed(tree, true), "Collapse all"));
            }

            toolbar.With(KUIButton.Icon(KUIIcons.Plus, () => AddCategory(catalog, null), "New category"));
            column.Add(toolbar);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;

            // Right-clicking the empty space below the tree offers the one command that does not
            // belong to any row. It bubbles, so a right-click on a row gets the row's commands and
            // this one underneath them — which is what a file tree does.
            scroll.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (evt.menu.MenuItems().Count > 0) evt.menu.AppendSeparator();

                evt.menu.AppendAction("New Category…", _ => AddCategory(catalog, null));
            }));

            scroll.Add(BuildCategoryRow(catalog, null, "All keys", LocalizationStats.For(catalog).EntryCount, 0, false));

            foreach (var node in tree)
                AddCategoryRows(catalog, scroll, node, 0);

            column.Add(scroll);
            return column;
        }

        private static bool HasNesting(List<CategoryNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count > 0) return true;
            }

            return false;
        }

        private void SetAllCollapsed(List<CategoryNode> nodes, bool collapsed)
        {
            foreach (var node in nodes)
            {
                if (node.Children.Count > 0)
                {
                    if (collapsed) m_Collapsed.Add(node.Path);
                    else m_Collapsed.Remove(node.Path);
                }

                SetAllCollapsed(node.Children, collapsed);
            }

            m_Window.Refresh();
        }

        /// <summary>
        /// Unfolds everything above a path, so a category that was just created or selected is
        /// actually on screen rather than hidden inside a collapsed parent.
        /// </summary>
        private void Reveal(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            for (var i = 0; i < path.Length; i++)
            {
                if (path[i] == LocalizationKeys.Separator)
                    m_Collapsed.Remove(path.Substring(0, i));
            }

            m_Collapsed.Remove(path);
        }

        /// <summary>One level of the category tree, with the level below it hanging off it.</summary>
        private sealed class CategoryNode
        {
            internal string Segment;                    // "Quit Level"
            internal string Path;                       // "Popups/Quit Level"
            internal LocalizationCategory Category;     // null for a group nothing is filed under
            internal int Total;                         // entries in this node and everything under it

            internal readonly List<CategoryNode> Children = new List<CategoryNode>();
        }

        /// <summary>
        /// Turns the catalog's flat list of category paths into the tree the paths describe.
        /// </summary>
        /// <remarks>
        /// Intermediate levels are inferred: a catalog holding only <c>Popups/Quit</c> and
        /// <c>Popups/Rate</c> has no category called <c>Popups</c>, but the sidebar has to show one
        /// or the two are orphans. Such a node carries no <see cref="CategoryNode.Category"/>, and
        /// the operations that need a real category are left off it.
        /// </remarks>
        private static List<CategoryNode> BuildCategoryTree(LocalizationCatalog catalog)
        {
            var roots = new List<CategoryNode>();
            var byPath = new Dictionary<string, CategoryNode>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < catalog.Categories.Count; i++)
            {
                var category = catalog.Categories[i];
                if (string.IsNullOrEmpty(category.Name)) continue;

                var segments = category.Name.Split(LocalizationKeys.Separator);
                var siblings = roots;
                var path = string.Empty;
                CategoryNode node = null;

                foreach (var segment in segments)
                {
                    path = path.Length == 0 ? segment : path + LocalizationKeys.Separator + segment;

                    if (byPath.TryGetValue(path, out node))
                    {
                        // Categories compare case-insensitively, so "popups/Rate" belongs to the
                        // branch "Popups/Quit" already made. Carrying the existing node's spelling
                        // forward keeps the whole subtree under one path rather than forking it.
                        path = node.Path;
                    }
                    else
                    {
                        node = new CategoryNode { Segment = segment, Path = path };
                        byPath[path] = node;
                        siblings.Add(node);
                    }

                    // The count is added on the way down, so a group totals its whole subtree
                    // without a second pass.
                    node.Total += category.Entries.Count;
                    siblings = node.Children;
                }

                if (node != null) node.Category = category;
            }

            return roots;
        }

        private void AddCategoryRows(
            LocalizationCatalog catalog,
            VisualElement container,
            CategoryNode node,
            int depth)
        {
            container.Add(BuildCategoryRow(catalog, node, node.Segment, node.Total, depth, true));

            if (node.Children.Count > 0 && !m_Collapsed.Contains(node.Path))
            {
                foreach (var child in node.Children)
                    AddCategoryRows(catalog, container, child, depth + 1);
            }
        }

        private VisualElement BuildCategoryRow(
            LocalizationCatalog catalog,
            CategoryNode node,
            string label,
            int count,
            int depth,
            bool indent)
        {
            var path = node?.Path;

            var row = new KUIListRow(label, () =>
            {
                m_Category = path;
                m_Window.Refresh();
            });

            row.Selected = string.Equals(m_Category, path, StringComparison.OrdinalIgnoreCase);
            row.tooltip = path;

            if (indent)
                row.style.paddingLeft = 8f + (depth * 12f);

            // The twist is always laid out, blank on a leaf, so labels at the same depth line up
            // whether or not their neighbours have children.
            row.hierarchy.Insert(0, BuildTwist(node));

            row.WithBadge(count.ToString());

            if (node == null) return row;

            row.WithAction(KUIDropdownButton.Overflow(menu =>
            {
                menu.Item("New Subcategory…", () => AddCategory(catalog, node.Path));

                // A group is a path, not a stored object: there is nothing to rename or delete
                // until something is actually filed under that exact path.
                menu.Item("Rename…", () => RenameCategory(catalog, node), node.Category != null, false);
                menu.Separator();
                menu.Item(DeleteLabel(node), () => DeleteCategory(catalog, node));
            }));

            // The same commands on right-click, because that is where anyone who has used a file
            // tree looks for them first — the ⋮ is for finding out they exist.
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                m_Category = node.Path;

                evt.menu.AppendAction("New Subcategory…", _ => AddCategory(catalog, node.Path));
                evt.menu.AppendAction(
                    "Rename…",
                    _ => RenameCategory(catalog, node),
                    node.Category != null ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

                evt.menu.AppendSeparator();
                evt.menu.AppendAction(DeleteLabel(node), _ => DeleteCategory(catalog, node));
            }));

            if (node.Children.Count > 0)
            {
                // Double-click folds. Single-click already means select, and a file tree gives you
                // both without making you hit the arrow every time.
                row.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (evt.button != 0 || evt.clickCount < 2) return;

                    ToggleCollapsed(node.Path);
                    evt.StopPropagation();
                });
            }

            return row;
        }

        private static string DeleteLabel(CategoryNode node) =>
            node.Children.Count > 0 ? "Delete (with subcategories)" : "Delete";

        private void ToggleCollapsed(string path)
        {
            if (!m_Collapsed.Remove(path)) m_Collapsed.Add(path);

            m_Window.Refresh();
        }

        /// <summary>The expand/collapse arrow, or a blank of the same width on a leaf.</summary>
        private VisualElement BuildTwist(CategoryNode node)
        {
            var twist = new Label();
            twist.style.width = 12f;
            twist.style.minWidth = 12f;
            twist.style.flexShrink = 0;
            twist.style.fontSize = 8f;
            twist.style.unityTextAlign = TextAnchor.MiddleLeft;

            if (node == null || node.Children.Count == 0) return twist;

            var collapsed = m_Collapsed.Contains(node.Path);
            twist.text = collapsed ? KUIIcons.ArrowRight : KUIIcons.ArrowDown;

            twist.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button != 0) return;

                // Stopped here so the click folds the branch instead of also selecting it: those
                // are different intentions and the arrow is the one that means "fold".
                evt.StopPropagation();
                ToggleCollapsed(node.Path);
            });

            return twist;
        }

        // ---------------------------------------------------------------- keys

        private VisualElement BuildKeyPane(LocalizationCatalog catalog)
        {
            var column = new VisualElement();
            column.style.flexGrow = 1;
            column.style.minHeight = 0;

            column.Add(BuildKeyToolbar(catalog));

            // The list and the translations are split rather than stacked so the translations can
            // never be squeezed out of the window: a catalog with a dozen languages needs far more
            // room below than a fixed height can promise, and how the space is divided is the
            // user's call, not a constant's.
            var split = new KUISplitView(220f, true, "LocalizationKit.KeysDetailSplit");
            split.style.flexGrow = 1;

            split.First.Add(BuildKeyTable(catalog));
            split.Second.Add(BuildDetailPane(catalog));

            column.Add(split);
            return column;
        }

        private VisualElement BuildKeyToolbar(LocalizationCatalog catalog)
        {
            var toolbar = new KUIToolbar();

            var search = new KUISearchField("Search keys and text…", value =>
            {
                m_Search = value ?? string.Empty;
                RefreshList(catalog);
            }, 0f);

            toolbar.With(search.Grow());
            toolbar.With(new KUIToggleSwitch("Only missing", m_OnlyMissing, value =>
            {
                m_OnlyMissing = value;
                RefreshList(catalog);
            }));

            m_Count = KUIText.Muted(string.Empty);
            m_Count.style.marginLeft = 6f;
            m_Count.style.marginRight = 6f;
            m_Count.style.whiteSpace = WhiteSpace.NoWrap;
            toolbar.With(m_Count);

            toolbar.With(KUIButton.Secondary("+ Key", BeginNewKey));
            return toolbar;
        }

        private VisualElement BuildKeyTable(LocalizationCatalog catalog)
        {
            var table = new VisualElement();
            table.style.flexGrow = 1;
            table.style.minHeight = 0;

            table.Add(BuildKeyTableHeader(catalog));

            RebuildVisible(catalog);

            m_List = new KUIVirtualList<EntryRef>(
                m_Visible,
                makeRow: () => new KeyRow(this, catalog),
                bindRow: (element, item, index) => BindRow(catalog, (KeyRow)element, item, index),
                rowHeight: k_RowHeight);

            m_List.EmptyMessage = m_OnlyMissing
                ? "Every key in this view is translated."
                : "No keys match.";

            m_List.SelectionChanged += index =>
            {
                if ((uint)index >= (uint)m_Visible.Count) return;

                m_SelectedKey = m_Visible[index].FullKey;
                RebuildDetail(catalog);
            };

            m_List.style.flexGrow = 1;
            m_List.style.minHeight = 0;

            m_List.SelectedIndex = IndexOfSelected();

            table.Add(m_List);
            UpdateCount();

            return table;
        }

        /// <summary>
        /// The column titles. Laid out with the same metrics as a row, so the header and the rows
        /// stay in step when the pane is resized.
        /// </summary>
        private VisualElement BuildKeyTableHeader(LocalizationCatalog catalog)
        {
            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0;
            header.style.paddingLeft = 8f;
            header.style.paddingRight = 8f;
            header.style.marginBottom = 2f;

            var key = HeaderLabel("Key");
            Column(key, k_KeyFlex);

            var term = HeaderLabel(DefaultLanguageName(catalog));
            Column(term, k_TermFlex);
            term.style.marginLeft = 10f;

            var status = HeaderLabel("Status");
            status.style.width = k_StatusWidth;
            status.style.minWidth = k_StatusWidth;
            status.style.flexShrink = 0;
            status.style.unityTextAlign = TextAnchor.MiddleRight;

            header.Add(key);
            header.Add(term);
            header.Add(status);

            return header;
        }

        private static Label HeaderLabel(string text)
        {
            var label = KUIText.SectionTitle(text);

            // SectionTitle carries a 10px top margin meant for a heading above a card; inside a
            // table header it would push the titles off the row.
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.marginLeft = 0f;
            label.style.marginRight = 0f;
            label.style.overflow = Overflow.Hidden;
            label.style.textOverflow = TextOverflow.Ellipsis;
            label.style.whiteSpace = WhiteSpace.NoWrap;

            return label;
        }

        /// <summary>Sizes an element as a proportional table column that clips rather than wraps.</summary>
        private static void Column(VisualElement element, float flex)
        {
            element.style.flexGrow = flex;
            element.style.flexShrink = 1f;
            element.style.flexBasis = 0f;
            element.style.minWidth = 0f;
        }

        private static string DefaultLanguageName(LocalizationCatalog catalog)
        {
            var index = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
            if (index < 0) index = 0;

            return index < catalog.Languages.Count ? catalog.Languages[index].DisplayName : "Term";
        }

        /// <summary>
        /// A recycled row: key on the left, the default language's text beside it, coverage on the
        /// right. Every visual it owns is created once and only ever reassigned, which is what
        /// makes it safe to rebind while scrolling.
        /// </summary>
        private sealed class KeyRow : VisualElement
        {
            internal readonly Label Key = new Label();
            internal readonly Label Term = new Label();
            internal readonly KUIBadge Badge = new KUIBadge(string.Empty).Outline();

            /// <summary>
            /// What this element is currently showing. Read by the context menu, which opens long
            /// after the row was built and must not close over the item it was built with.
            /// </summary>
            internal EntryRef Item;

            internal KeyRow(LocalizationKeysPage page, LocalizationCatalog catalog)
            {
                AddToClassList(KUIClass.ListItem);
                style.flexDirection = FlexDirection.Row;
                style.alignItems = Align.Center;

                // Belt and braces against the overlap this row is shaped to avoid: even if a child
                // did manage to render taller than the row, it would be clipped rather than drawn
                // over the row below.
                style.overflow = Overflow.Hidden;

                Key.AddToClassList(KUIClass.ListItemLabel);
                Clip(Key);
                Column(Key, k_KeyFlex);

                Term.AddToClassList(KUIClass.ListItemSublabel);
                Clip(Term);
                Column(Term, k_TermFlex);
                Term.style.marginLeft = 10f;

                var status = new VisualElement();
                status.style.width = k_StatusWidth;
                status.style.minWidth = k_StatusWidth;
                status.style.flexShrink = 0;
                status.style.flexDirection = FlexDirection.Row;
                status.style.justifyContent = Justify.FlexEnd;
                status.Add(Badge);

                Add(Key);
                Add(Term);
                Add(status);

                this.AddManipulator(new ContextualMenuManipulator(
                    evt => page.PopulateRowMenu(evt, catalog, Item)));
            }

            private static void Clip(Label label)
            {
                label.style.whiteSpace = WhiteSpace.NoWrap;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
            }
        }

        private void BindRow(LocalizationCatalog catalog, KeyRow row, EntryRef item, int index)
        {
            var fullKey = item.FullKey;
            var defaultLanguage = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);
            var term = defaultLanguage >= 0 ? item.Entry.GetValue(defaultLanguage) : null;

            row.Item = item;
            row.Key.text = fullKey;
            row.Key.tooltip = fullKey;

            row.Term.text = string.IsNullOrEmpty(term) ? KUIIcons.EmDash : term;
            row.Term.tooltip = term;

            var missing = CountMissing(catalog, item.Entry);
            row.Badge.text = missing == 0 ? KUIIcons.Check : missing.ToString();
            row.Badge.Tone = missing == 0 ? KUITone.Success : KUITone.Warning;
            row.Badge.tooltip = missing == 0
                ? "Translated into every language."
                : $"Missing in {missing} language{(missing == 1 ? string.Empty : "s")}.";

            // Reset, not toggle: this element showed a different key a moment ago.
            row.EnableInClassList(KUIClass.ListItemOdd, index % 2 == 1);
            row.EnableInClassList(KUIClass.ListItemSelected, string.Equals(fullKey, m_SelectedKey, StringComparison.Ordinal));
        }

        private static int CountMissing(LocalizationCatalog catalog, LocalizationEntry entry)
        {
            var missing = 0;
            for (var i = 0; i < catalog.Languages.Count; i++)
                if (entry.IsMissing(i)) missing++;

            return missing;
        }

        private void RebuildVisible(LocalizationCatalog catalog)
        {
            m_Visible.Clear();

            var search = m_Search?.Trim();
            var hasSearch = !string.IsNullOrEmpty(search);

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                // Subtree, not exact match: picking Popups has to show what is in Popups/Quit too,
                // or selecting a group in the tree would show an empty list.
                if (m_Category != null && !LocalizationKeys.IsUnder(category.Name, m_Category))
                    continue;

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null || string.IsNullOrEmpty(entry.Key)) continue;

                    if (m_OnlyMissing && CountMissing(catalog, entry) == 0) continue;

                    if (hasSearch && !Matches(catalog, category, entry, search)) continue;

                    m_Visible.Add(new EntryRef(category, entry));
                }
            }
        }

        /// <summary>Search covers the key and every translation, so you can find a key by its English.</summary>
        private static bool Matches(
            LocalizationCatalog catalog,
            LocalizationCategory category,
            LocalizationEntry entry,
            string search)
        {
            var fullKey = LocalizationKeys.Compose(category.Name, entry.Key);
            if (fullKey.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0) return true;

            for (var i = 0; i < catalog.Languages.Count; i++)
            {
                var value = entry.GetValue(i);
                if (!string.IsNullOrEmpty(value) && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private void RefreshList(LocalizationCatalog catalog)
        {
            RebuildVisible(catalog);
            m_List?.SetItems(m_Visible);

            if (m_List != null)
                m_List.SelectedIndex = IndexOfSelected();

            UpdateCount();
        }

        private int IndexOfSelected()
        {
            if (string.IsNullOrEmpty(m_SelectedKey)) return -1;

            for (var i = 0; i < m_Visible.Count; i++)
            {
                if (string.Equals(m_Visible[i].FullKey, m_SelectedKey, StringComparison.Ordinal))
                    return i;
            }

            return -1;
        }

        private void UpdateCount()
        {
            if (m_Count == null) return;

            m_Count.text = m_Visible.Count == 1 ? "1 key" : $"{m_Visible.Count} keys";
        }

        // ---------------------------------------------------------------- detail

        private VisualElement BuildDetailPane(LocalizationCatalog catalog)
        {
            m_Detail = new VisualElement();
            m_Detail.style.flexGrow = 1;
            m_Detail.style.minHeight = 0;
            m_Detail.style.paddingTop = 6f;

            RebuildDetail(catalog);
            return m_Detail;
        }

        private void RebuildDetail(LocalizationCatalog catalog)
        {
            if (m_Detail == null) return;

            m_Detail.Clear();

            var selected = FindSelected(catalog);
            if (selected == null)
            {
                var hint = new VisualElement();
                hint.style.flexGrow = 1;
                hint.style.alignItems = Align.Center;
                hint.style.justifyContent = Justify.Center;
                hint.Add(KUIText.Muted("Select a key to translate it."));

                m_Detail.Add(hint);
                return;
            }

            var reference = selected.Value;

            // The title and its actions stay put while the languages scroll under them: with a
            // dozen languages the key being edited would otherwise be the first thing to leave the
            // screen.
            m_Detail.Add(BuildDetailHeader(catalog, reference));
            m_Detail.Add(BuildDetailBody(catalog, reference));
        }

        private VisualElement BuildDetailHeader(LocalizationCatalog catalog, EntryRef reference)
        {
            var entry = reference.Entry;
            var missing = CountMissing(catalog, entry);

            var title = new Label(reference.FullKey);
            title.AddToClassList(KUIClass.Title2);
            title.style.marginBottom = 0f;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.flexShrink = 1f;
            title.style.minWidth = 0f;
            title.tooltip = reference.FullKey;

            var badge = new KUIBadge(
                missing == 0 ? "complete" : $"{missing} missing",
                missing == 0 ? KUITone.Success : KUITone.Warning).Outline();
            badge.style.marginLeft = 8f;
            badge.style.flexShrink = 0;

            var header = KUILayout.Row(title, badge, KUILayout.Spacer());
            header.style.flexShrink = 0;
            header.style.paddingLeft = 2f;
            header.style.paddingRight = 2f;
            header.style.marginBottom = 4f;

            header.Add(KUIButton.Icon(
                "⧉",
                () => EditorGUIUtility.systemCopyBuffer = reference.FullKey,
                "Copy the full key to the clipboard."));

            header.Add(KUIDropdownButton.Overflow(menu => PopulateEntryMenu(menu, catalog, reference)));

            return header;
        }

        private VisualElement BuildDetailBody(LocalizationCatalog catalog, EntryRef reference)
        {
            var entry = reference.Entry;

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1;
            scroll.style.minHeight = 0;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;

            // The key and its category are editable here rather than only behind a dialog: they
            // are the two things you most often want to see while translating, and a field you can
            // read is a field you can check against the code that uses it.
            scroll.Add(DetailRow("Key", BuildKeyNameField(catalog, reference)));
            scroll.Add(DetailRow("Category", BuildCategoryPicker(catalog, reference)));

            var description = new TextField { value = entry.Description ?? string.Empty };
            description.RegisterValueChangedCallback(e =>
            {
                LocalizationEditorCatalog.RecordUndo(catalog, "Edit Description");
                entry.Description = e.newValue;
                MarkDirty(catalog);
            });

            scroll.Add(DetailRow("Note", description));
            scroll.Add(KUILayout.Separator());

            var defaultLanguage = catalog.IndexOfLanguage(catalog.DefaultLanguageCode);

            for (var i = 0; i < catalog.Languages.Count; i++)
                scroll.Add(BuildLanguageRow(catalog, reference, i, defaultLanguage));

            return scroll;
        }

        /// <summary>
        /// A labelled line of the detail pane. Every label is the same width so the key, the note
        /// and every language field start at the same x.
        /// </summary>
        private static VisualElement DetailRow(string label, VisualElement control, VisualElement trailing = null)
        {
            var text = new Label(label);
            text.AddToClassList(KUIClass.KeyValueKey);
            text.style.width = k_DetailLabelWidth;
            text.style.minWidth = k_DetailLabelWidth;
            text.style.marginTop = 3f;
            text.style.whiteSpace = WhiteSpace.NoWrap;
            text.style.overflow = Overflow.Hidden;
            text.style.textOverflow = TextOverflow.Ellipsis;

            control.style.flexGrow = 1;
            control.style.flexShrink = 1;
            control.style.minWidth = 0;
            control.style.marginLeft = 0f;
            control.style.marginRight = 0f;

            var row = KUILayout.Row(text, control, trailing);
            row.style.alignItems = Align.FlexStart;
            row.style.marginBottom = 3f;
            row.style.minWidth = 0;

            return row;
        }

        /// <summary>
        /// The key's own name, renamed on Enter or when focus leaves — never per keystroke, which
        /// would rename the entry once per character typed and fill the undo stack with rubbish.
        /// </summary>
        private VisualElement BuildKeyNameField(LocalizationCatalog catalog, EntryRef reference)
        {
            var field = new TextField { value = reference.Entry.Key, isDelayed = true };

            field.RegisterValueChangedCallback(e =>
            {
                var next = (e.newValue ?? string.Empty).Trim();
                if (string.Equals(next, reference.Entry.Key, StringComparison.Ordinal)) return;

                var error = string.IsNullOrWhiteSpace(next)
                    ? "A key cannot be blank."
                    : Validate(catalog, reference.Category.Name, next, reference.FullKey);

                if (error != null)
                {
                    m_Window.Toast(error, KUITone.Error);
                    field.SetValueWithoutNotify(reference.Entry.Key);
                    return;
                }

                LocalizationEditorCatalog.RecordUndo(catalog, "Rename Key");
                reference.Entry.Key = next;
                m_SelectedKey = LocalizationKeys.Compose(reference.Category.Name, next);

                m_Window.SaveCatalog();
                m_Window.Toast("Key renamed. Fields referencing the old key now point at nothing.", KUITone.Warning);
                RefreshLater();
            });

            return field;
        }

        /// <summary>The category the key sits in. Choosing another one moves it.</summary>
        private VisualElement BuildCategoryPicker(LocalizationCatalog catalog, EntryRef reference)
        {
            return KUIDropdownButton.Create(
                reference.Category.Name,
                menu => PopulateCategoryMenu(menu, catalog, reference),
                "Move this key to another category.");
        }

        /// <summary>
        /// One language's text. The language name is a fixed-width column rather than the text
        /// field's own label, so every field starts at the same x whatever the language is called.
        /// </summary>
        private VisualElement BuildLanguageRow(
            LocalizationCatalog catalog,
            EntryRef reference,
            int languageIndex,
            int defaultLanguage)
        {
            var entry = reference.Entry;
            var language = catalog.Languages[languageIndex];
            var isDefault = languageIndex == defaultLanguage;

            var field = new TextField
            {
                value = entry.GetValue(languageIndex) ?? string.Empty,
                multiline = true,
            };

            field.style.flexGrow = 1;
            field.style.flexShrink = 1;
            field.style.minWidth = 0;
            field.style.marginLeft = 0f;
            field.style.marginRight = 0f;

            // Right-to-left languages are unreadable in a left-aligned field: the punctuation ends
            // up on the wrong side of the line and translators cannot proof their own work.
            if (language.RightToLeft)
                field.style.unityTextAlign = TextAnchor.UpperRight;

            field.RegisterValueChangedCallback(e =>
            {
                LocalizationEditorCatalog.RecordUndo(catalog, "Edit Translation");
                entry.SetValue(languageIndex, e.newValue);
                MarkDirty(catalog);

                // The row's term column and coverage badge are now stale.
                m_List?.RefreshVisible();
            });

            // A trailing cell of a fixed width, present whether or not it holds anything, so the
            // fields do not jump sideways as a language is filled in.
            var trailing = new VisualElement();
            trailing.style.width = 74f;
            trailing.style.minWidth = 74f;
            trailing.style.flexShrink = 0;
            trailing.style.flexDirection = FlexDirection.Row;
            trailing.style.justifyContent = Justify.FlexEnd;
            trailing.style.marginTop = 2f;

            if (entry.IsMissing(languageIndex))
            {
                // Seeding an empty language from the default beats retyping it, and it is the
                // usual first step when a translator works from the source text.
                if (!isDefault && defaultLanguage >= 0 && !entry.IsMissing(defaultLanguage))
                {
                    trailing.Add(KUIButton.Ghost("copy source", () =>
                    {
                        LocalizationEditorCatalog.RecordUndo(catalog, "Copy Translation");
                        entry.SetValue(languageIndex, entry.GetValue(defaultLanguage));
                        MarkDirty(catalog);

                        m_List?.RefreshVisible();
                        RebuildDetail(catalog);
                    }).Tip($"Fill this in with the {catalog.Languages[defaultLanguage].DisplayName} text."));
                }
                else
                {
                    trailing.Add(new KUIBadge("missing", KUITone.Warning).Outline());
                }
            }

            var row = DetailRow(language.DisplayName, field, trailing);

            var label = row.Q<Label>(className: KUIClass.KeyValueKey);
            label.tooltip = isDefault
                ? $"{language.DisplayName} ({language.Code}) — the default language"
                : $"{language.DisplayName} ({language.Code})";

            if (isDefault) label.style.unityFontStyleAndWeight = FontStyle.Bold;

            return row;
        }

        private EntryRef? FindSelected(LocalizationCatalog catalog)
        {
            if (string.IsNullOrEmpty(m_SelectedKey)) return null;

            for (var c = 0; c < catalog.Categories.Count; c++)
            {
                var category = catalog.Categories[c];

                for (var e = 0; e < category.Entries.Count; e++)
                {
                    var entry = category.Entries[e];
                    if (entry == null) continue;

                    if (string.Equals(LocalizationKeys.Compose(category.Name, entry.Key), m_SelectedKey, StringComparison.Ordinal))
                        return new EntryRef(category, entry);
                }
            }

            return null;
        }

        // ---------------------------------------------------------------- operations

        /// <summary>Saves without rebuilding the page, so a text field keeps focus while typing.</summary>
        private void MarkDirty(LocalizationCatalog catalog)
        {
            EditorUtility.SetDirty(catalog);
        }

        internal void BeginNewKey()
        {
            var catalog = LocalizationEditorCatalog.Catalog;
            if (catalog == null) return;

            var startingCategory = m_Category ?? LocalizationKeys.DefaultCategory;

            LocalizationKeyDialog.Open(
                "New Key",
                "Keys are addressed as Category/Key. Pick an existing category or make a new one.",
                startingCategory,
                string.Empty,
                CategoryNames(catalog),
                (category, key) => Validate(catalog, category, key, null),
                (category, key) =>
                {
                    LocalizationEditorCatalog.RecordUndo(catalog, "Add Key");
                    catalog.AddEntry(category, key);

                    m_SelectedKey = LocalizationKeys.Compose(category, key);
                    m_Category = null;

                    m_Window.SaveCatalog();
                    m_Window.Toast($"Added {m_SelectedKey}.");
                    m_Window.Refresh();
                });
        }

        private void RenameEntry(LocalizationCatalog catalog, EntryRef reference)
        {
            LocalizationKeyDialog.Open(
                "Rename Key",
                "Anything already pointing at the old key stops resolving.",
                reference.Category.Name,
                reference.Entry.Key,
                CategoryNames(catalog),
                (category, key) => Validate(catalog, category, key, reference.FullKey),
                (category, key) =>
                {
                    LocalizationEditorCatalog.RecordUndo(catalog, "Rename Key");

                    // A rename is a move when the category changed: the entry object carries its
                    // translations, so it is relocated rather than copied.
                    if (!string.Equals(category, reference.Category.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        reference.Category.Entries.Remove(reference.Entry);
                        catalog.GetOrAddCategory(category).Entries.Add(reference.Entry);
                    }

                    reference.Entry.Key = key;
                    m_SelectedKey = LocalizationKeys.Compose(category, key);

                    m_Window.SaveCatalog();
                    m_Window.Toast("Key renamed. Fields referencing the old key now point at nothing.", KUITone.Warning);
                    m_Window.Refresh();
                });
        }

        /// <summary>
        /// Why a category and key cannot be used, or null when they can. Runs as the user types,
        /// so the dialog can refuse before the modal closes rather than after.
        /// </summary>
        /// <param name="allow">A full key that is not a clash, i.e. the one being renamed.</param>
        private static string Validate(LocalizationCatalog catalog, string category, string key, string allow)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;   // Nothing typed yet is not an error.
            if (!LocalizationKeys.IsValidName(key)) return "A key cannot contain '/'.";

            var full = LocalizationKeys.Compose(category, key);

            if (!string.Equals(full, allow, StringComparison.Ordinal) && catalog.FindByFullKey(full) != null)
                return $"'{full}' already exists.";

            return null;
        }

        /// <summary>The commands that act on one key, shared by the overflow button and right-click.</summary>
        private void PopulateEntryMenu(KUIMenu menu, LocalizationCatalog catalog, EntryRef reference)
        {
            menu.Item("Rename Key…", () => RenameEntry(catalog, reference));
            menu.Item("Duplicate Key…", () => DuplicateEntry(catalog, reference));

            // A submenu rather than another dialog: moving a key is a one-click decision as soon
            // as the destinations are on screen, and the dialog only ever added a round trip.
            var categories = CategoryNames(catalog);

            foreach (var name in categories)
            {
                var target = name;
                menu.Item(
                    "Move to/" + MenuPathOf(categories, target),
                    () => MoveEntryTo(catalog, reference, target),
                    on: string.Equals(target, reference.Category.Name, StringComparison.OrdinalIgnoreCase));
            }

            menu.Item("Move to/New Category…", () => MoveToNewCategory(catalog, reference));
            menu.Item("Copy Key", () => EditorGUIUtility.systemCopyBuffer = reference.FullKey);
            menu.Separator();
            menu.Item("Delete Key", () => DeleteEntry(catalog, reference));
        }

        /// <summary>The category list behind the detail pane's category button.</summary>
        private void PopulateCategoryMenu(KUIMenu menu, LocalizationCatalog catalog, EntryRef reference)
        {
            var categories = CategoryNames(catalog);

            foreach (var name in categories)
            {
                var target = name;
                menu.Item(
                    MenuPathOf(categories, target),
                    () => MoveEntryTo(catalog, reference, target),
                    on: string.Equals(target, reference.Category.Name, StringComparison.OrdinalIgnoreCase));
            }

            menu.Separator();
            menu.Item("New Category…", () => MoveToNewCategory(catalog, reference));
        }

        /// <summary>Right-click on a row. Selects it first, so the menu acts on what was clicked.</summary>
        private void PopulateRowMenu(ContextualMenuPopulateEvent evt, LocalizationCatalog catalog, EntryRef reference)
        {
            if (reference.Entry == null) return;

            m_SelectedKey = reference.FullKey;
            m_List?.RefreshVisible();
            RebuildDetail(catalog);

            evt.menu.AppendAction("Rename Key…", _ => RenameEntry(catalog, reference));
            evt.menu.AppendAction("Duplicate Key…", _ => DuplicateEntry(catalog, reference));

            var categories = CategoryNames(catalog);

            foreach (var name in categories)
            {
                var target = name;
                var current = string.Equals(target, reference.Category.Name, StringComparison.OrdinalIgnoreCase);

                evt.menu.AppendAction(
                    "Move to/" + MenuPathOf(categories, target),
                    _ => MoveEntryTo(catalog, reference, target),
                    current ? DropdownMenuAction.Status.Checked : DropdownMenuAction.Status.Normal);
            }

            evt.menu.AppendAction("Move to/New Category…", _ => MoveToNewCategory(catalog, reference));
            evt.menu.AppendAction("Copy Key", _ => EditorGUIUtility.systemCopyBuffer = reference.FullKey);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Delete Key", _ => DeleteEntry(catalog, reference));
        }

        /// <summary>Relocates a key, translations and all. The entry object moves; nothing is copied.</summary>
        private void MoveEntryTo(LocalizationCatalog catalog, EntryRef reference, string target)
        {
            if (string.Equals(target, reference.Category.Name, StringComparison.OrdinalIgnoreCase)) return;

            var full = LocalizationKeys.Compose(target, reference.Entry.Key);

            // Two entries resolving to the same full key is unrecoverable from the UI — the second
            // one can never be looked up — so the move is refused rather than allowed to happen.
            if (catalog.FindByFullKey(full) != null)
            {
                m_Window.Toast($"'{full}' already exists.", KUITone.Error);
                return;
            }

            LocalizationEditorCatalog.RecordUndo(catalog, "Move Key");
            reference.Category.Entries.Remove(reference.Entry);
            catalog.GetOrAddCategory(target).Entries.Add(reference.Entry);

            m_SelectedKey = full;

            m_Window.SaveCatalog();
            m_Window.Toast($"Moved to {target}.");
            RefreshLater();
        }

        private void MoveToNewCategory(LocalizationCatalog catalog, EntryRef reference)
        {
            LocalizationTextDialog.Open(
                "Move to New Category",
                $"Creates the category and moves {reference.FullKey} into it.",
                "Name",
                string.Empty,
                name => ValidateCategory(catalog, name, null),
                name =>
                {
                    // Creating the category and moving into it are one undo step: undoing a move
                    // that leaves an empty category behind is not what anyone means by undo.
                    LocalizationEditorCatalog.RecordUndo(catalog, "Move Key");

                    reference.Category.Entries.Remove(reference.Entry);
                    catalog.GetOrAddCategory(name).Entries.Add(reference.Entry);

                    m_SelectedKey = LocalizationKeys.Compose(name, reference.Entry.Key);

                    m_Window.SaveCatalog();
                    m_Window.Toast($"Moved to {name}.");
                    m_Window.Refresh();
                });
        }

        /// <summary>
        /// Copies a key with every translation it has. The usual reason is a second string that is
        /// nearly the first — a variant of a popup, a plural form — and retyping five languages to
        /// change one word in each is how translations drift apart.
        /// </summary>
        private void DuplicateEntry(LocalizationCatalog catalog, EntryRef reference)
        {
            var source = reference.Entry;

            LocalizationKeyDialog.Open(
                "Duplicate Key",
                "The copy carries every translation and the note for translators.",
                reference.Category.Name,
                SuggestCopyName(catalog, reference),
                CategoryNames(catalog),
                (category, key) => Validate(catalog, category, key, null),
                (category, key) =>
                {
                    LocalizationEditorCatalog.RecordUndo(catalog, "Duplicate Key");

                    var copy = catalog.AddEntry(category, key);
                    copy.Description = source.Description;

                    for (var i = 0; i < catalog.Languages.Count; i++)
                        copy.SetValue(i, source.GetValue(i));

                    m_SelectedKey = LocalizationKeys.Compose(category, key);
                    m_Category = null;

                    m_Window.SaveCatalog();
                    m_Window.Toast($"Duplicated to {m_SelectedKey}.");
                    m_Window.Refresh();
                });
        }

        /// <summary>The first free <c>KeyCopy</c>, <c>KeyCopy2</c>… name in the key's own category.</summary>
        private static string SuggestCopyName(LocalizationCatalog catalog, EntryRef reference)
        {
            for (var attempt = 1; attempt < 100; attempt++)
            {
                var candidate = reference.Entry.Key + "Copy" + (attempt == 1 ? string.Empty : attempt.ToString());

                if (catalog.FindByFullKey(LocalizationKeys.Compose(reference.Category.Name, candidate)) == null)
                    return candidate;
            }

            return reference.Entry.Key + "Copy";
        }

        /// <summary>
        /// Rebuilds the page after the current event has finished. Refreshing inline would destroy
        /// the very field or menu whose callback is still running.
        /// </summary>
        private void RefreshLater()
        {
            EditorApplication.delayCall += () => m_Window.Refresh();
        }

        private void DeleteEntry(LocalizationCatalog catalog, EntryRef reference)
        {
            var confirmed = EditorUtility.DisplayDialog(
                $"Delete {reference.FullKey}?",
                "Every translation of this key goes with it, and any field pointing at it will stop resolving.",
                "Delete",
                "Cancel");

            if (!confirmed) return;

            LocalizationEditorCatalog.RecordUndo(catalog, "Delete Key");
            reference.Category.Entries.Remove(reference.Entry);

            if (string.Equals(m_SelectedKey, reference.FullKey, StringComparison.Ordinal))
                m_SelectedKey = null;

            m_Window.SaveCatalog();
            m_Window.Toast("Key deleted.", KUITone.Warning);
            m_Window.Refresh();
        }

        /// <summary>Creates a category, optionally underneath an existing one.</summary>
        /// <param name="parent">The path the new category hangs off, or null for a top-level one.</param>
        private void AddCategory(LocalizationCatalog catalog, string parent)
        {
            var nested = !string.IsNullOrEmpty(parent);

            LocalizationTextDialog.Open(
                nested ? "New Subcategory" : "New Category",
                nested
                    ? $"Created under {parent}, so its keys are addressed as {parent}/Name/Key."
                    : "Categories nest with '/' — Popups, Popups/Quit Level, Store/Bundles.",
                "Name",
                string.Empty,
                name => ValidateCategory(catalog, Join(parent, name), null),
                name =>
                {
                    var path = Join(parent, name);

                    LocalizationEditorCatalog.RecordUndo(catalog, "Add Category");
                    catalog.GetOrAddCategory(path);

                    m_Category = path;
                    Reveal(path);

                    m_Window.SaveCatalog();
                    m_Window.Toast($"Created {path}.");
                    m_Window.Refresh();
                });
        }

        /// <summary>
        /// Renames a category and, with it, everything nested under it.
        /// </summary>
        /// <remarks>
        /// A subcategory is a prefix of its parent's path and nothing else — there is no parent
        /// object holding a list of children — so renaming <c>Popups</c> has to rewrite the prefix
        /// of <c>Popups/Quit Level</c> too, or the branch is orphaned at a path that no longer has
        /// a parent.
        /// </remarks>
        private void RenameCategory(LocalizationCatalog catalog, CategoryNode node)
        {
            if (node.Category == null) return;

            var oldPath = node.Path;
            var parent = ParentOf(oldPath);

            LocalizationTextDialog.Open(
                "Rename Category",
                node.Children.Count > 0
                    ? "Every key in this category and its subcategories is addressed by the new name from now on."
                    : "Every key in this category is addressed by the new name from now on.",
                "Name",
                node.Segment,
                name => ValidateCategory(catalog, Join(parent, name), oldPath),
                name =>
                {
                    var newPath = Join(parent, name);

                    LocalizationEditorCatalog.RecordUndo(catalog, "Rename Category");

                    var renamed = 0;
                    for (var i = 0; i < catalog.Categories.Count; i++)
                    {
                        var category = catalog.Categories[i];
                        if (!LocalizationKeys.IsUnder(category.Name, oldPath)) continue;

                        category.Name = newPath + category.Name.Substring(oldPath.Length);
                        renamed++;
                    }

                    if (m_Category != null && LocalizationKeys.IsUnder(m_Category, oldPath))
                        m_Category = newPath + m_Category.Substring(oldPath.Length);

                    // The fold state is keyed by path, so every folded branch under the old name
                    // would otherwise be orphaned and silently spring open.
                    Refold(oldPath, newPath);
                    Reveal(newPath);

                    m_Window.SaveCatalog();
                    m_Window.Toast(
                        renamed == 1
                            ? "Category renamed. Every key in it changed too."
                            : $"{renamed} categories renamed. Every key in them changed too.",
                        KUITone.Warning);
                    m_Window.Refresh();
                });
        }

        /// <summary>Moves the folded-branch bookkeeping along with a renamed subtree.</summary>
        private void Refold(string oldPath, string newPath)
        {
            var moved = new List<string>();

            foreach (var path in m_Collapsed)
            {
                if (LocalizationKeys.IsUnder(path, oldPath)) moved.Add(path);
            }

            foreach (var path in moved)
            {
                m_Collapsed.Remove(path);
                m_Collapsed.Add(newPath + path.Substring(oldPath.Length));
            }
        }

        /// <summary>Why a category path cannot be used, or null when it can.</summary>
        /// <param name="allow">A path that is not a clash, i.e. the one being renamed.</param>
        private static string ValidateCategory(LocalizationCatalog catalog, string path, string allow)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (!LocalizationKeys.IsValidCategory(path))
                return "A category cannot start or end with '/', or have an empty part.";

            if (!string.Equals(path, allow, StringComparison.OrdinalIgnoreCase) && catalog.FindCategory(path) != null)
                return $"'{path}' already exists.";

            return null;
        }

        private void DeleteCategory(LocalizationCatalog catalog, CategoryNode node)
        {
            var doomed = new List<LocalizationCategory>();
            var keys = 0;

            for (var i = 0; i < catalog.Categories.Count; i++)
            {
                var category = catalog.Categories[i];
                if (!LocalizationKeys.IsUnder(category.Name, node.Path)) continue;

                doomed.Add(category);
                keys += category.Entries.Count;
            }

            var scope = doomed.Count > 1
                ? $"{doomed.Count} categories"
                : $"'{node.Path}'";

            var confirmed = EditorUtility.DisplayDialog(
                $"Delete {node.Path}?",
                keys == 0
                    ? $"This deletes {scope}, which hold no keys."
                    : $"This deletes {scope} — {keys} key{(keys == 1 ? string.Empty : "s")} and every translation in them.",
                "Delete",
                "Cancel");

            if (!confirmed) return;

            LocalizationEditorCatalog.RecordUndo(catalog, "Delete Category");

            foreach (var category in doomed)
                catalog.RemoveCategory(category.Name);

            if (m_Category != null && LocalizationKeys.IsUnder(m_Category, node.Path))
                m_Category = null;

            m_Window.SaveCatalog();
            m_Window.Toast("Category deleted.", KUITone.Warning);
            m_Window.Refresh();
        }

        /// <summary>Joins a parent path and a segment, tolerating a null or empty parent.</summary>
        private static string Join(string parent, string segment) =>
            string.IsNullOrEmpty(parent)
                ? (segment ?? string.Empty).Trim()
                : parent + LocalizationKeys.Separator + (segment ?? string.Empty).Trim();

        /// <summary>Everything before a path's last segment, or null when it has only one.</summary>
        private static string ParentOf(string path)
        {
            var slash = path == null ? -1 : path.LastIndexOf(LocalizationKeys.Separator);
            return slash <= 0 ? null : path.Substring(0, slash);
        }

        /// <summary>
        /// Every category a key can go in — the stored ones and the base categories they imply.
        /// </summary>
        private static List<string> CategoryNames(LocalizationCatalog catalog)
        {
            var names = LocalizationEditorCatalog.CategoryPaths(catalog);

            if (names.Count == 0) names.Add(LocalizationKeys.DefaultCategory);

            return names;
        }

        private static string MenuPathOf(List<string> categories, string name) =>
            LocalizationDialog.CategoryMenuPath(categories, name);
    }
}
