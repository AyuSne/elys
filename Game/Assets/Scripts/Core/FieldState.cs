namespace ElysFarm.Core
{
    /// <summary>
    /// 地块的五种状态（见 GLOSSARY：地块 5 态）。
    /// </summary>
    public enum FieldState
    {
        /// <summary>空地，可以播种。</summary>
        Empty,

        /// <summary>已播种，尚未经历一次生长结算。</summary>
        Sown,

        /// <summary>生长中。</summary>
        Growing,

        /// <summary>成熟，可以收获。</summary>
        Mature,

        /// <summary>已收获，需要再播种。</summary>
        Harvested
    }
}
