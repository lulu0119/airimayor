namespace CS2MCP
{
    /// <summary>How a timed simulation wait ended.</summary>
    public enum WaitOutcome
    {
        None,
        Finished,
        Cancelled,
        TakenOver,
        Stalled,
    }
}
