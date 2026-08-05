using System;

namespace Lume
{
    public class NoteData
    {
        public string Title { get; set; } = "新笔记";
        public string DateCreated { get; set; } = DateTime.Now.ToString("yyyy/MM/dd");

        // 兼容老版本的旧数据结构
        public string ContentRtf { get; set; } = "";

        // 新增：高性能纯文本保存字段
        public string ContentText { get; set; } = "";
    }
}