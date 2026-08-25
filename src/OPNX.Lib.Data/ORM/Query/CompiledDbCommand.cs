namespace OPNX.Lib.Data.ORM.Query;

public sealed class CompiledDbCommand(string commandText, List<KeyValuePair<string, object>> parameters)
{
    public string CommandText { get; } = commandText;
    public List<KeyValuePair<string, object>> Parameters { get; } = parameters;
}
