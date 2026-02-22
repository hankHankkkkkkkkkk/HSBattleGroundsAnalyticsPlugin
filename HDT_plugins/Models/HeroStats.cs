using System;
using System.Collections.Generic;
using System.Text;

namespace HDTplugins.Models
{
    /// <summary>
    /// 单个英雄的统计数据
    /// </summary>
    public class HeroStats
    {
        /// <summary>选择次数</summary>
        public int Picks { get; set; }

        /// <summary>胜场次数（默认Top4算胜，后面可改成Top2/Top4）</summary>
        public int Wins { get; set; }

        /// <summary>吃鸡次数（第1名）</summary>
        public int Firsts { get; set; }

        /// <summary>名次总和（用来计算平均名次）</summary>
        public int TotalPlacement { get; set; }

        /// <summary>
        /// 名次分布：1~8名分别出现了几次
        /// 注意：C# 7.3 不能写 new()，要写 new Dictionary<int,int>()
        /// </summary>
        public Dictionary<int, int> PlacementHistogram { get; set; }
            = new Dictionary<int, int>();
    }
}
