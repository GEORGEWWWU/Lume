using ICSharpCode.AvalonEdit.Rendering;
using ICSharpCode.AvalonEdit;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Lume
{
    public class TodoElementGenerator : VisualLineElementGenerator
    {
        private readonly TextEditor _editor;

        public TodoElementGenerator(TextEditor editor)
        {
            _editor = editor;
        }

        public override int GetFirstInterestedOffset(int startOffset)
        {
            var document = CurrentContext.Document;
            int endOffset = CurrentContext.VisualLine.LastDocumentLine.EndOffset;

            // 搜索当前行是否存在 "- [ ] " 或 "- [x] "（刚好 6 个字符）
            for (int i = startOffset; i <= endOffset - 6; i++)
            {
                char c = document.GetCharAt(i);
                if (c == '-')
                {
                    string text = document.GetText(i, 6);
                    if (text == "- [ ] " || text.ToLower() == "- [x] ")
                    {
                        return i;
                    }
                }
            }
            return -1;
        }

        public override VisualLineElement ConstructElement(int offset)
        {
            var document = CurrentContext.Document;
            string text = document.GetText(offset, 6);
            bool isChecked = text.ToLower() == "- [x] ";

            var anchor = document.CreateAnchor(offset);
            anchor.SurviveDeletion = false;

            // ==========================================
            // 🌟 核心修复：实时获取当前字号，动态计算缩放
            // ==========================================
            double currentFontSize = 15.0; // 兜底默认字号
            if (CurrentContext != null && CurrentContext.GlobalTextRunProperties != null)
            {
                currentFontSize = CurrentContext.GlobalTextRunProperties.FontRenderingEmSize;
            }

            // 设定比例：框框大小大概是字号的 1.06 倍视觉上最舒服 (16 / 15 ≈ 1.06)
            double boxSize = currentFontSize * 1.06;
            // 设定比例：字变大了，打勾的线条也要跟着变粗，才不会显得单薄
            double strokeThick = 1.5 * (currentFontSize / 15.0);
            // 设定比例：跟随字号动态计算上下偏移量（解决不同缩放比例下对齐跑偏的问题）
            double yOffset = currentFontSize * 0.15;

            string uncheckedPath = "M3,3 L13,3 C14.1,3 15,3.9 15,5 L15,15 C15,16.1 14.1,17 13,17 L3,17 C1.9,17 1,16.1 1,15 L1,5 C1,3.9 1.9,3 3,3 Z";
            string checkedPath = uncheckedPath + " M4.5,10.5 L7.5,13.5 L12.5,5.5";

            var path = new Path
            {
                Data = Geometry.Parse(isChecked ? checkedPath : uncheckedPath),
                Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isChecked ? "#27C93F" : "#A0A0A0")),

                // 应用动态计算的线条粗细和大小
                StrokeThickness = strokeThick,
                Width = boxSize,
                Height = boxSize,

                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Fill = Brushes.Transparent,
                Stretch = Stretch.Uniform
            };

            var border = new Border
            {
                Child = path,
                Cursor = Cursors.Hand,
                Background = Brushes.Transparent,
                // 右边距固定，上下边距使用动态计算的值，确保任意缩放都居中
                Margin = new Thickness(0, yOffset, 6, -yOffset),
                VerticalAlignment = VerticalAlignment.Center
            };

            border.MouseLeftButtonDown += (s, e) =>
            {
                if (anchor.IsDeleted) return;

                string newText = isChecked ? "- [ ] " : "- [x] ";
                _editor.Document.Replace(anchor.Offset, 6, newText);

                e.Handled = true;
            };

            return new InlineObjectElement(6, border);
        }
    }
}