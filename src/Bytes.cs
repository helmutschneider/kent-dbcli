namespace Kent.DbCli;

public sealed record Bytes(string Literal, int Value)
{
    public override string ToString() => Literal;
}
