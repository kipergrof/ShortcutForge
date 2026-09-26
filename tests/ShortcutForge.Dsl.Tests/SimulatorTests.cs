using ShortcutForge.Core.Simulation;
using ShortcutForge.Dsl;
using Xunit;

namespace ShortcutForge.Dsl.Tests;

public class SimulatorTests
{
    private sealed class Host : ISimulationHost
    {
        public Queue<string?> Answers = new();
        public Queue<int?> Choices = new();
        public bool AlertOk = true;
        public List<string> Asked = [];

        public string? AskText(string prompt, string? defaultAnswer)
        {
            Asked.Add(prompt);
            return Answers.Count > 0 ? Answers.Dequeue() : defaultAnswer;
        }

        public int? Choose(string prompt, IReadOnlyList<string> items) => Choices.Count > 0 ? Choices.Dequeue() : 0;

        public bool Alert(string title, string message, bool cancelShown) => AlertOk;
    }

    private static SimulationResult Run(string code, Host? host = null, string? input = null) =>
        new ShortcutSimulator(host ?? new Host()).Run(DslParser.Parse(code), input);

    [Fact]
    public void Text_variables_and_conditions_run()
    {
        var result = Run("""
            greeting = Text("Good morning, {ShortcutInput}!")
            if greeting contains "morning" {
                Notification(greeting, title: "Hello")
            } else {
                Alert("Not morning")
            }
            """, input: "Kriszti");
        Assert.Null(result.StopReason);
        Assert.Equal("Good morning, Kriszti!", result.Steps[0].Output);
        Assert.Contains(result.Steps, s => s.Kind == SimStepKind.Simulated && s.Note!.Contains("Good morning, Kriszti!"));
        Assert.DoesNotContain(result.Steps, s => s.Action == "Show Alert");
    }

    [Fact]
    public void Loops_collect_results_and_math_works()
    {
        var result = Run("""
            numbers = List([1, 2, 3])
            foreach numbers {
                doubled = Calculate(RepeatItem, operation: "×", operand: 2)
                var results += doubled
            }
            total = CombineText(results, separator: "Spaces")
            """);
        Assert.Null(result.StopReason);
        Assert.Equal("2 4 6", result.Steps[^1].Output);
    }

    [Fact]
    public void Repeat_and_named_variables()
    {
        var result = Run("""
            repeat 3 {
                var items += RepeatIndex
            }
            n = Count(items)
            """);
        Assert.Equal("3", result.Steps[^1].Output);
    }

    [Fact]
    public void Ask_and_menu_use_the_host()
    {
        var host = new Host();
        host.Answers.Enqueue("41");
        host.Choices.Enqueue(1);
        var result = Run("""
            answer = AskForInput("How many?", type: "Number")
            plus = Calculate(answer, operation: "+", operand: 1)
            menu "Pick" {
            case "One" {
                Text("first")
            }
            case "Two" {
                Text("second")
            }
            }
            """, host);
        Assert.Contains("How many?", host.Asked);
        Assert.Equal("42", result.Steps[1].Output);
        Assert.Equal("second", result.Steps[^1].Output);
    }

    [Fact]
    public void Cancelling_stops_the_shortcut()
    {
        var host = new Host { AlertOk = false };
        var result = Run("""
            Alert("Continue?")
            Text("after")
            """, host);
        Assert.NotNull(result.StopReason);
        Assert.Single(result.Steps); // only the alert itself; "after" never ran
        Assert.NotNull(result.Steps[0].Note);
    }

    [Fact]
    public void Device_actions_are_skipped_with_a_placeholder()
    {
        var result = Run("""
            data = GetContentsOfUrl("https://example.com/api")
            Text("got {data}")
            """);
        Assert.Equal(SimStepKind.Skipped, result.Steps[0].Kind);
        Assert.StartsWith("got ‹", result.Steps[1].Output);
    }

    [Fact]
    public void Dictionaries_and_keys()
    {
        var result = Run("""
            person = Dictionary({"name": "Anna", "age": 30})
            name = GetDictionaryValue(person, key: "name")
            Text("{person["age"]}")
            """);
        Assert.Equal("Anna", result.Steps[1].Output);
        Assert.Equal("30", result.Steps[2].Output);
    }

    [Fact]
    public void Endless_loops_are_cut_off()
    {
        var result = Run("""
            repeat 100000 {
                Nothing()
            }
            """);
        Assert.NotNull(result.StopReason);
        Assert.True(result.Steps.Count <= ShortcutSimulator.MaxSteps + 1);
    }
}
