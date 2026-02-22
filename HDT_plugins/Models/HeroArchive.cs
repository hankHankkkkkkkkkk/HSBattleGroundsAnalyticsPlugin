using System;
using System.Collections.Generic;
using System.Text;

namespace HDTplugins.Models
{
    /// <summary>
    /// 一个“归档文件”的数据结构
    /// 用于保存某个 season-patch（或自定义归档名）下所有英雄的统计数据
    /// </summary>
    public class HeroArchive
    {
        /// <summary>
        /// 归档Key，例如：season12-34.6.0（后面我们会自动生成）
        /// </summary>
        public string ArchiveKey { get; set; } = "";

        /// <summary>
        /// 最近更新时间（UTC时间）
        /// </summary>
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 英雄统计字典：key=英雄CardId，value=该英雄的统计信息
        /// 注意：C# 7.3 不能写 new()，要写 new Dictionary<...>()
        /// </summary>
        public Dictionary<string, HeroStats> Heroes { get; set; }
            = new Dictionary<string, HeroStats>();
    }
}