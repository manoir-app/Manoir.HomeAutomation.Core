using System;
using System.Text;

namespace Home.Common.Model;

public enum ConditionKind
{
    Or,
    And,
    EntityCheck,
    UserCheck,
    DeviceCheck,
    RoomPropertyCheck,
    MeshPropertyCheck,
    SceneCheck,
}

public class Condition
{
    public ConditionKind Kind { get; set; }
    public Condition[] SubConditions { get; set; } = [];
    public string ElementId { get; set; }
    public string Operator { get; set; }
    public string PropertyName { get; set; }
    public string[] InValues { get; set; }
    public string Value { get; set; }
    public TimeSpan? MinDuration { get; set; }
    public TimeSpan? MaxDuration { get; set; }

    public override string ToString()
    {
        Normalize();

        StringBuilder builder = new StringBuilder();
        ToStringBuilder(builder, 0);
        return builder.ToString();
    }

    public void Normalize()
    {
        if (string.IsNullOrEmpty(Operator))
            Operator = "==";
        if ("=".Equals(Operator, StringComparison.Ordinal))
            Operator = "==";
        if ("<>".Equals(Operator, StringComparison.Ordinal))
            Operator = "!=";

        if (Operator.Equals("!=", StringComparison.Ordinal)
            && Value != null
            && bool.TryParse(Value, out bool booleanValue))
        {
            Operator = "==";
            Value = (!booleanValue).ToString().ToLowerInvariant();
        }
    }

    private void ToStringBuilder(StringBuilder builder, int indent)
    {
        builder.Append(new string(' ', indent));

        switch (Kind)
        {
            case ConditionKind.Or:
                builder.AppendLine("OR (");
                AppendSubConditions(builder, indent);
                break;
            case ConditionKind.And:
                builder.AppendLine("AND (");
                AppendSubConditions(builder, indent);
                break;
            case ConditionKind.DeviceCheck:
                builder.Append("Device(");
                builder.Append(ElementId);
                builder.Append(")");
                AddOperatorAndValues(builder);
                break;
            case ConditionKind.UserCheck:
                builder.Append("User(");
                builder.Append(ElementId);
                builder.Append(")");
                AddOperatorAndValues(builder);
                break;
            case ConditionKind.RoomPropertyCheck:
                builder.Append("Room(");
                builder.Append(ElementId);
                builder.Append(")");
                AddOperatorAndValues(builder);
                break;
            case ConditionKind.MeshPropertyCheck:
                builder.Append("Mesh(");
                builder.Append(ElementId ?? "local");
                builder.Append(")");
                AddOperatorAndValues(builder);
                break;
            case ConditionKind.SceneCheck:
                builder.Append("Scene(");
                builder.Append(ElementId ?? "??");
                builder.Append(")");
                AddOperatorAndValues(builder);
                break;
            default:
                builder.Append(Kind.ToString());
                break;
        }
    }

    private void AppendSubConditions(StringBuilder builder, int indent)
    {
        if (SubConditions != null)
        {
            for (int index = 0; index < SubConditions.Length; index++)
            {
                if (index > 0)
                    builder.AppendLine();

                SubConditions[index].ToStringBuilder(builder, indent + 2);
            }
        }

        builder.AppendLine();
        builder.Append(new string(' ', indent));
        builder.AppendLine(")");
    }

    private void AddOperatorAndValues(StringBuilder builder)
    {
        builder.Append('.');
        builder.Append(PropertyName);
        builder.Append(Operator ?? " ");

        if (InValues != null && InValues.Length > 0)
        {
            builder.Append('(');
            for (int index = 0; index < InValues.Length; index++)
            {
                if (index > 0)
                    builder.Append(',');

                builder.Append(InValues[index]);
            }

            builder.Append(')');
            return;
        }

        builder.Append(Value ?? "-null-");
    }
}