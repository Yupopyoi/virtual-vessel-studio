using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace VirtualVessel.UI.Controls
{
    /// <summary>
    /// A page's own table of contents (components.md 3.29): a link per Card on the page, under the page's Tabs. A link
    /// scrolls its Card to the top of the screen, opening it if it is closed. Long pages had become hard to move through
    /// (user's request, 2026-11-22).
    /// </summary>
    public sealed class PageIndex : VisualElement
    {
        public const string UssClass = "vv-page-index";
        public const string LinkClass = UssClass + "__link";

        /// <summary>Space left above a Card it scrolls to, so the Card's top edge stays in sight.</summary>
        public const float TopMargin = 16f;

        private readonly List<Card> _targets = new List<Card>();

        public PageIndex()
        {
            AddToClassList(UssClass);
        }

        /// <summary>The links, in the order of the Cards; for tests.</summary>
        public IReadOnlyList<Button> Links => Children().OfType<Button>().ToList();

        /// <summary>Makes a link for each Card that has a title, in order. With fewer than two, the index hides.</summary>
        public void SetCards(IEnumerable<Card> cards)
        {
            Clear();
            _targets.Clear();
            foreach (Card card in (cards ?? Enumerable.Empty<Card>()).Where(card => card != null && !string.IsNullOrEmpty(card.TitleKey)))
            {
                Card target = card;
                var link = new Button(() => JumpTo(target));
                link.AddToClassList(LinkClass);
                link.AddToClassList("vv-text-body");
                new KeyedText(link, text => link.text = text).Key = card.TitleKey;
                FocusRing.Attach(link);
                Add(link);
                _targets.Add(card);
            }

            style.display = _targets.Count >= 2 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>The scroll offset that puts a Card's top at <see cref="TopMargin"/> below the view's top. Pure.</summary>
        public static float OffsetFor(float cardTop, float contentTop, float maxOffset)
        {
            return Mathf.Clamp(cardTop - contentTop - TopMargin, 0f, Mathf.Max(0f, maxOffset));
        }

        /// <summary>The Cards of a page in order: each child that is a Card, or the first Card inside it. Pure.</summary>
        public static IEnumerable<Card> CardsOf(VisualElement page)
        {
            if (page == null)
            {
                yield break;
            }

            foreach (VisualElement child in page.Children())
            {
                Card card = child as Card ?? child.Q<Card>();
                if (card != null)
                {
                    yield return card;
                }
            }
        }

        private void JumpTo(Card card)
        {
            if (card.Collapsible)
            {
                card.Expanded = true;
            }

            ScrollView scroll = GetFirstAncestorOfType<ScrollView>();
            if (scroll == null)
            {
                return;
            }

            // After the layout, since opening a Card changes the heights below it.
            scroll.schedule.Execute(() =>
            {
                float max = scroll.contentContainer.layout.height - scroll.contentViewport.layout.height;
                scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, OffsetFor(card.worldBound.yMin, scroll.contentContainer.worldBound.yMin, max));
                if (card.Header.focusable)
                {
                    card.Header.Focus();
                }
            });
        }
    }
}
