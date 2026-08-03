using System;

namespace Lume
{
    public class NoteData
    {
        public string Title { get; set; } = "新笔记";
        public string DateCreated { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");
        // 用于保存富文本内容的 RTF 字符串（支持文本、图片、基础表格等）
        public string ContentRtf { get; set; } = "";
    }
}