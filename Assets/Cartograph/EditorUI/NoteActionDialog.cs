using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Cartograph.EditorUI
{
    /// <summary>A runtime modal window; callers keep ownership of note storage and validation.</summary>
    public sealed class NoteActionDialog : VisualElement
    {
        private readonly Label heading, description, error;
        private readonly TextField title;
        private readonly Button submitButton, cancelButton;
        private Func<Task> submit;
        private VisualElement previousFocus;
        private bool busy;
        public bool IsOpen => style.display.value != DisplayStyle.None;

        public NoteActionDialog()
        {
            name = "note-action-dialog";
            style.position = Position.Absolute;
            style.left = style.right = style.top = style.bottom = 0;
            style.alignItems = Align.Center;
            style.justifyContent = Justify.Center;
            style.backgroundColor = new Color(0, 0, 0, .66f);
            style.display = DisplayStyle.None;
            pickingMode = PickingMode.Position;

            var window = new VisualElement { name = "note-action-window" };
            window.style.width = 430; window.style.maxWidth = Length.Percent(94);
            window.style.paddingLeft = window.style.paddingRight = 24;
            window.style.paddingTop = window.style.paddingBottom = 22;
            window.style.backgroundColor = new Color(.105f, .13f, .19f);
            window.style.borderTopLeftRadius = window.style.borderTopRightRadius = 10;
            window.style.borderBottomLeftRadius = window.style.borderBottomRightRadius = 10;
            Add(window);
            heading = new Label(); heading.style.fontSize = 21;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.color = Color.white; window.Add(heading);
            description = new Label { enableRichText = false };
            description.style.whiteSpace = WhiteSpace.Normal;
            description.style.marginTop = 12; description.style.marginBottom = 12;
            description.style.color = new Color(.78f, .82f, .88f); window.Add(description);
            title = new TextField { name = "rename-note-title" };
            title.style.marginBottom = 10; window.Add(title);
            error = new Label { enableRichText = false };
            error.style.whiteSpace = WhiteSpace.Normal;
            error.style.color = new Color(1, .55f, .5f);
            error.style.marginBottom = 10; window.Add(error);
            var actions = new VisualElement(); actions.style.flexDirection = FlexDirection.Row;
            actions.style.justifyContent = Justify.FlexEnd; window.Add(actions);
            cancelButton = new Button(Close) { text = "キャンセル" };
            submitButton = new Button(Submit) { text = "変更する" };
            actions.Add(cancelButton); actions.Add(submitButton);
            RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                { Close(); evt.StopImmediatePropagation(); panel?.focusController.IgnoreEvent(evt); }
                else if (evt.keyCode == KeyCode.Tab)
                {
                    var focused = panel?.focusController.focusedElement as VisualElement;
                    bool inTitle = focused != null && (focused == title || title.Contains(focused));
                    VisualElement next;
                    if (title.style.display.value == DisplayStyle.None)
                        next = focused == cancelButton ? submitButton : cancelButton;
                    else if (evt.shiftKey)
                        next = inTitle ? (VisualElement)submitButton : focused == submitButton ? cancelButton : title;
                    else
                        next = inTitle ? (VisualElement)cancelButton : focused == cancelButton ? submitButton : title;
                    if (!busy) next.Focus();
                    evt.StopImmediatePropagation(); panel?.focusController.IgnoreEvent(evt);
                }
            }, TrickleDown.TrickleDown);
            // Keep keyboard navigation inside this window even if the underlying view stays visible.
            RegisterCallback<NavigationMoveEvent>(evt => evt.StopPropagation());
            RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            RegisterCallback<WheelEvent>(evt => evt.StopPropagation());
        }

        public void ShowRename(string initial, Func<string, Task> onSubmit)
        {
            if (onSubmit == null) throw new ArgumentNullException(nameof(onSubmit));
            if (busy) return;
            heading.text = "ノートの名前を変更";
            description.text = "新しい名前を入力してください。";
            title.style.display = DisplayStyle.Flex;
            title.SetValueWithoutNotify(initial ?? "");
            submitButton.text = "変更する";
            submit = () =>
            {
                string value = title.value.Trim();
                if (value.Length == 0) throw new ArgumentException("ノートの名前を入力してください。");
                return onSubmit(value);
            };
            Show(title);
        }

        public void ShowDelete(string noteTitle, Func<Task> onSubmit)
        {
            if (onSubmit == null) throw new ArgumentNullException(nameof(onSubmit));
            if (busy) return;
            heading.text = "ノートを削除しますか？";
            description.text = "「" + (noteTitle ?? "") + "」を削除します。";
            title.style.display = DisplayStyle.None;
            submitButton.text = "削除する";
            submit = onSubmit;
            Show(cancelButton);
        }

        private void Show(VisualElement focus)
        {
            if (!IsOpen) previousFocus = panel?.focusController.focusedElement as VisualElement;
            error.text = "";
            style.display = DisplayStyle.Flex;
            BringToFront();
            schedule.Execute(() => { if (IsOpen && !busy) { focus.Focus(); if (focus == title) title.SelectAll(); } });
        }

        public void Close()
        {
            if (busy) return;
            style.display = DisplayStyle.None;
            submit = null;
            if (previousFocus?.panel != null) previousFocus.Focus();
            previousFocus = null;
        }

        private async void Submit()
        {
            if (busy || submit == null) return;
            busy = true; error.text = "";
            title.SetEnabled(false); submitButton.SetEnabled(false); cancelButton.SetEnabled(false);
            bool succeeded = false;
            try { await submit(); succeeded = true; }
            catch (Exception exception) { error.text = exception.Message; }
            finally
            {
                busy = false;
                title.SetEnabled(true); submitButton.SetEnabled(true); cancelButton.SetEnabled(true);
            }
            if (succeeded) Close();
            else if (title.style.display.value != DisplayStyle.None) title.Focus();
            else cancelButton.Focus();
        }
    }
}
