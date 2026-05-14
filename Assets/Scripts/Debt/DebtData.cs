public class DebtData
{
    public int Cycle { get; }
    public int Amount { get; }

    public DebtData(int cycle, int amount)
    {
        Cycle = cycle;
        Amount = amount;
    }
}
