using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using VirtualVessel.UI.Icons;
using VirtualVessel.UI.Tooltips;

namespace VirtualVessel.UI.Controls
{
    /// <summary>One entry of a <see cref="PopupMenu"/>; <see cref="Separator"/> draws a line instead.</summary>
    public sealed class MenuEntry
    {
        public static readonly MenuEntry Separator = new MenuEntry();

        public MenuEntry(string textKey, Action invoke, string shortcut = null, string tooltipKey = null, bool? isChecked = null, bool enabled = true)
        {
            TextKey = textKey ?? throw new ArgumentNullException(nameof(textKey));
            Invoke = invoke ?? throw new ArgumentNullException(nameof(invoke));
            Shortcut = shortcut;
            TooltipKey = tooltipKey;
            Checked = isChecked;
            Enabled = enabled;
        }

        private MenuEntry()
        {
        }

        public bool IsSeparator => TextKey == null;

        public string TextKey { get; }

        public Action Invoke { get; }

        /// <summary>Shown at the right as a key hint, such as "Shift+1".</summary>
        public string Shortcut { get; }

        /// <summary>What the entry does, shown as its tooltip (UI foundation detailed design 9.4).</summary>
        public string TooltipKey { get; }

        /// <summary>For on/off entries: true shows a check mark; null for plain commands.</summary>
        public bool? Checked { get; }

        public bool Enabled { get; }
    }

    /// <summary>
    /// A menu that opens at a point, such as a right-click menu (components.md 3.23). It opens on the
    /// nearest themed root, takes keyboard focus, and closes when an entry is chosen, on Esc, or on a
    /// press outside it.
    /// </summary>
    public sealed class PopupMenu : VisualElement
    {
        public const string UssClass = "vv-menu";

        public const string ItemClass = UssClass + "__item";

        public const string SeparatorClass = UssClass + "__separator";

        public const string HighlightClass = ItemClass + "--highlight";

        private readonly List<Item> _items = new List<Item>();
        private VisualElement _root;
        private Action _closed;
        private int _highlight = -1;

        private PopupMenu(IReadOnlyList<MenuEntry> entries)
        {
            AddToClassList(UssClass);
            style.position = Position.Absolute;
            focusable = true;
            foreach (MenuEntry entry in entries)
            {
                if (entry.IsSeparator)
                {
                    var separator = new VisualElement();
                    separator.AddToClassList(SeparatorClass);
                    Add(separator);
                    continue;
                }

                var item = new Item(entry);
                item.RegisterCallback<PointerEnterEvent>(_ =>
                {
                    if (item.Entry.Enabled)
                    {
                        Highlight(_items.IndexOf(item));
                    }
                });
                item.RegisterCallback<ClickEvent>(_ => Choose(item));
                _items.Add(item);
                Add(item);
            }

            RegisterCallback<NavigationMoveEvent>(OnNavigationMove);
            RegisterCallback<NavigationSubmitEvent>(evt =>
            {
                if (_highlight >= 0)
                {
                    Choose(_items[_highlight]);
                }

                evt.StopPropagation();
            });
            RegisterCallback<NavigationCancelEvent>(evt =>
            {
                Close();
                evt.StopPropagation();
            });
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                    evt.StopPropagation();
                }
            });
            RegisterCallback<GeometryChangedEvent>(_ => Reposition());
        }

        public bool IsOpen => _root != null;

        public IReadOnlyList<VisualElement> Items => _items;

        /// <summary>The entry the keyboard is on, or -1.</summary>
        public int HighlightIndex => _highlight;

        private Vector2 Anchor { get; set; }

        /// <summary>
        /// Opens a menu at <paramref name="panelPosition"/> on the themed root above
        /// <paramref name="owner"/>. <paramref name="closed"/> runs once when it closes, for returning focus.
        /// </summary>
        public static PopupMenu Show(VisualElement owner, Vector2 panelPosition, IReadOnlyList<MenuEntry> entries, Action closed = null)
        {
            if (owner == null)
            {
                throw new ArgumentNullException(nameof(owner));
            }

            VisualElement root = Controls.ThemedRoot.Of(owner);
            var menu = new PopupMenu(entries ?? throw new ArgumentNullException(nameof(entries)))
            {
                _root = root,
                _closed = closed,
            };
            menu.Anchor = root.WorldToLocal(panelPosition);
            menu.style.left = menu.Anchor.x;
            menu.style.top = menu.Anchor.y;
            root.Add(menu);
            root.RegisterCallback<PointerDownEvent>(menu.OnRootPointerDown, TrickleDown.TrickleDown);
            menu.Highlight(menu.NextEnabled(-1, 1));

            // After the event that opened it: a pointer press focuses its target when it finishes.
            menu.schedule.Execute(menu.Focus);
            return menu;
        }

        /// <summary>
        /// The top-left corner for a menu of <paramref name="size"/> opened at <paramref name="at"/>:
        /// below and to the right, flipped up or left when it would leave <paramref name="bounds"/>. Pure.
        /// </summary>
        public static Vector2 Place(Vector2 at, Vector2 size, Rect bounds, float margin)
        {
            float x = at.x + size.x > bounds.xMax - margin ? at.x - size.x : at.x;
            float y = at.y + size.y > bounds.yMax - margin ? at.y - size.y : at.y;
            x = Mathf.Max(bounds.xMin + margin, Mathf.Min(x, bounds.xMax - margin - size.x));
            y = Mathf.Max(bounds.yMin + margin, Mathf.Min(y, bounds.yMax - margin - size.y));
            return new Vector2(x, y);
        }

        public void Close()
        {
            if (_root == null)
            {
                return;
            }

            _root.UnregisterCallback<PointerDownEvent>(OnRootPointerDown, TrickleDown.TrickleDown);
            _root = null;
            RemoveFromHierarchy();
            Action closed = _closed;
            _closed = null;
            closed?.Invoke();
        }

        private void Choose(Item item)
        {
            if (!item.Entry.Enabled)
            {
                return;
            }

            Close();
            item.Entry.Invoke();
        }

        private void OnRootPointerDown(PointerDownEvent evt)
        {
            if (!(evt.target is VisualElement target) || !Contains(target))
            {
                Close();
            }
        }

        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            int step = evt.direction == NavigationMoveEvent.Direction.Down || evt.direction == NavigationMoveEvent.Direction.Next ? 1
                : evt.direction == NavigationMoveEvent.Direction.Up || evt.direction == NavigationMoveEvent.Direction.Previous ? -1
                : 0;
            if (step != 0)
            {
                Highlight(NextEnabled(_highlight, step));
            }

            // Focus stays in the menu until it closes.
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        private int NextEnabled(int from, int step)
        {
            for (int i = 1; i <= _items.Count; i++)
            {
                int index = (((from + (step * i)) % _items.Count) + _items.Count) % _items.Count;
                if (_items[index].Entry.Enabled)
                {
                    return index;
                }
            }

            return -1;
        }

        private void Highlight(int index)
        {
            _highlight = index;
            for (int i = 0; i < _items.Count; i++)
            {
                _items[i].EnableInClassList(HighlightClass, i == index);
            }
        }

        private void Reposition()
        {
            if (_root == null || float.IsNaN(layout.width))
            {
                return;
            }

            Vector2 position = Place(Anchor, layout.size, new Rect(Vector2.zero, _root.layout.size), 8f);
            style.left = position.x;
            style.top = position.y;
        }

        /// <summary>One row: a check mark when on, the text, and the shortcut.</summary>
        private sealed class Item : VisualElement, ITooltipSource
        {
            public Item(MenuEntry entry)
            {
                Entry = entry;
                AddToClassList(ItemClass);
                EnableInClassList(ItemClass + "--disabled", !entry.Enabled);
                var check = new Icon(entry.Checked == true ? IconName.Check : IconName.None) { pickingMode = PickingMode.Ignore };
                check.AddToClassList(UssClass + "__check");
                Add(check);
                Label text = QuietText.Of(new Label { pickingMode = PickingMode.Ignore });
                text.AddToClassList(UssClass + "__text");
                new KeyedText(text, value => text.text = value) { Key = entry.TextKey };
                Add(text);
                if (!string.IsNullOrEmpty(entry.Shortcut))
                {
                    var hint = new KeyHint(entry.Shortcut) { pickingMode = PickingMode.Ignore };
                    hint.AddToClassList(UssClass + "__shortcut");
                    Add(hint);
                }
            }

            public MenuEntry Entry { get; }

            string ITooltipSource.CurrentTooltipKey => Entry.TooltipKey;

            string ITooltipSource.Shortcut => null;
        }
    }
}
