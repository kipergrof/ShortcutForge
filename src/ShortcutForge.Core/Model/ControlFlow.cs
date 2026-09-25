using ShortcutForge.Core.Catalog;
using ShortcutForge.Core.Localization;

namespace ShortcutForge.Core.Model;

/// <summary>Helpers for control-flow blocks (If / Repeat / Repeat with Each / Choose from Menu).</summary>
public static class ControlFlow
{
    public const string IfId = "is.workflow.actions.conditional";
    public const string RepeatId = "is.workflow.actions.repeat.count";
    public const string RepeatEachId = "is.workflow.actions.repeat.each";
    public const string MenuId = "is.workflow.actions.choosefrommenu";

    /// <summary>Creates the actions making up a new, empty block (e.g. If + Otherwise + End If).</summary>
    public static List<ActionInstance> CreateBlock(ActionDefinition definition)
    {
        var group = ActionInstance.NewUuid();

        ActionInstance Make(ControlFlowMode mode)
        {
            var a = new ActionInstance(definition.Id) { GroupingIdentifier = group, ControlFlow = mode };
            if (mode == ControlFlowMode.End && definition.Output is not null) a.EnsureUuid();
            return a;
        }

        switch (definition.Control)
        {
            case ControlFlowKind.If:
            {
                var start = Make(ControlFlowMode.Start);
                start.Parameters["WFCondition"] = new IntegerValue(Conditions.HasAnyValue);
                return [start, Make(ControlFlowMode.Middle), Make(ControlFlowMode.End)];
            }
            case ControlFlowKind.Repeat:
            {
                var start = Make(ControlFlowMode.Start);
                start.Parameters["WFRepeatCount"] = new IntegerValue(1);
                return [start, Make(ControlFlowMode.End)];
            }
            case ControlFlowKind.RepeatEach:
                return [Make(ControlFlowMode.Start), Make(ControlFlowMode.End)];
            case ControlFlowKind.Menu:
            {
                var start = Make(ControlFlowMode.Start);
                start.Parameters["WFMenuItems"] = new ArrayValue([new StringValue(L.T("Egy", "One")), new StringValue(L.T("Kettő", "Two"))]);
                var one = Make(ControlFlowMode.Middle);
                one.Parameters["WFMenuItemTitle"] = new StringValue(L.T("Egy", "One"));
                var two = Make(ControlFlowMode.Middle);
                two.Parameters["WFMenuItemTitle"] = new StringValue(L.T("Kettő", "Two"));
                return [start, one, two, Make(ControlFlowMode.End)];
            }
            default:
                return [new ActionInstance(definition.Id)];
        }
    }

    /// <summary>Nesting depth of each action, for display (Otherwise / End are shown at the block's level).</summary>
    public static int[] ComputeIndent(IReadOnlyList<ActionInstance> actions)
    {
        var result = new int[actions.Count];
        var depth = 0;
        for (var i = 0; i < actions.Count; i++)
        {
            switch (actions[i].ControlFlow)
            {
                case ControlFlowMode.Start:
                    result[i] = depth++;
                    break;
                case ControlFlowMode.Middle:
                    result[i] = Math.Max(0, depth - 1);
                    break;
                case ControlFlowMode.End:
                    depth = Math.Max(0, depth - 1);
                    result[i] = depth;
                    break;
                default:
                    result[i] = depth;
                    break;
            }
        }
        return result;
    }

    /// <summary>
    /// Returns the index range [first, last] of the whole block that the action at
    /// <paramref name="index"/> belongs to, or (index, index) for a plain action.
    /// </summary>
    public static (int First, int Last) BlockRange(IReadOnlyList<ActionInstance> actions, int index)
    {
        var action = actions[index];
        if (action.ControlFlow is null || action.GroupingIdentifier is not { } group) return (index, index);

        int first = index, last = index;
        for (var i = 0; i < actions.Count; i++)
        {
            if (actions[i].GroupingIdentifier != group) continue;
            first = Math.Min(first, i);
            last = Math.Max(last, i);
        }
        return (first, last);
    }

    /// <summary>Checks that every block is opened and closed properly.</summary>
    public static string? Validate(IReadOnlyList<ActionInstance> actions)
    {
        var stack = new Stack<string>();
        for (var i = 0; i < actions.Count; i++)
        {
            var a = actions[i];
            switch (a.ControlFlow)
            {
                case ControlFlowMode.Start:
                    stack.Push(a.GroupingIdentifier ?? "");
                    break;
                case ControlFlowMode.Middle:
                    if (stack.Count == 0 || stack.Peek() != a.GroupingIdentifier)
                        return L.T($"A(z) {i + 1}. akció ({a.Identifier}) nem a saját blokkjában van.", $"Action {i + 1} ({a.Identifier}) is not inside its own block.");
                    break;
                case ControlFlowMode.End:
                    if (stack.Count == 0 || stack.Pop() != a.GroupingIdentifier)
                        return L.T($"A(z) {i + 1}. akció ({a.Identifier}) lezár egy nem nyitott blokkot.", $"Action {i + 1} ({a.Identifier}) closes a block that is not open.");
                    break;
            }
        }
        return stack.Count > 0 ? L.T("Van lezáratlan blokk (hiányzó 'vége' akció).", "There is an unclosed block (missing 'end' action).") : null;
    }
}

/// <summary>WFCondition codes of the If action.</summary>
public static class Conditions
{
    public const int LessThan = 0;
    public const int LessOrEqual = 1;
    public const int GreaterThan = 2;
    public const int GreaterOrEqual = 3;
    public const int Is = 4;
    public const int IsNot = 5;
    public const int BeginsWith = 8;
    public const int EndsWith = 9;
    public const int Contains = 99;
    public const int DoesNotContain = 999;
    public const int HasAnyValue = 100;
    public const int DoesNotHaveAnyValue = 101;
    public const int IsBetween = 1003;

    public static IReadOnlyList<(int Code, string Label)> All =>
    [
        (Is, L.T("egyenlő", "is")), (IsNot, L.T("nem egyenlő", "is not")), (Contains, L.T("tartalmazza", "contains")), (DoesNotContain, L.T("nem tartalmazza", "does not contain")),
        (BeginsWith, L.T("ezzel kezdődik", "begins with")), (EndsWith, L.T("ezzel végződik", "ends with")), (GreaterThan, L.T("nagyobb, mint", "is greater than")),
        (GreaterOrEqual, L.T("nagyobb vagy egyenlő", "is greater than or equal to")), (LessThan, L.T("kisebb, mint", "is less than")), (LessOrEqual, L.T("kisebb vagy egyenlő", "is less than or equal to")),
        (HasAnyValue, L.T("van értéke", "has any value")), (DoesNotHaveAnyValue, L.T("nincs értéke", "does not have any value")), (IsBetween, L.T("között van", "is between")),
    ];

    public static bool IsNumeric(int code) => code is LessThan or LessOrEqual or GreaterThan or GreaterOrEqual or IsBetween;

    public static bool IsUnary(int code) => code is HasAnyValue or DoesNotHaveAnyValue;
}
