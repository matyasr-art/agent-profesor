using AgentProfesor.Core;
using Xunit;

namespace AgentProfesor.Core.Tests;

public class LineDiffTests
{
    [Theory]
    [InlineData(new string[] { }, new string[] { })]
    [InlineData(new[] { "a" }, new[] { "a" })]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b", "c" })]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "x", "c" })]
    [InlineData(new[] { "a", "b", "c" }, new[] { "a", "b", "c", "d", "e" })]
    [InlineData(new[] { "a", "b", "c", "d", "e" }, new[] { "a", "c", "e" })]
    [InlineData(new[] { "a", "b" }, new[] { "b", "a" })]
    [InlineData(new string[] { }, new[] { "x", "y" })]
    [InlineData(new[] { "x", "y" }, new string[] { })]
    [InlineData(new[] { "same", "same", "same" }, new[] { "same", "same", "same" })]
    public void Apply_reconstructs_target_from_source(string[] source, string[] target)
    {
        var ops = LineDiff.Compute(source, target);
        var result = LineDiff.Apply(source, ops);
        Assert.Equal(target, result);
    }

    [Fact]
    public void Compute_of_identical_text_has_no_insert_or_delete()
    {
        var lines = new[] { "one", "two", "three" };
        var ops = LineDiff.Compute(lines, lines);
        Assert.All(ops, op => Assert.Equal(DiffOpType.Equal, op.Type));
    }

    [Fact]
    public void ChangedRatio_is_zero_for_no_change()
    {
        var lines = new[] { "one", "two", "three" };
        var ops = LineDiff.Compute(lines, lines);
        Assert.Equal(0.0, LineDiff.ChangedRatio(ops, lines.Length));
    }

    [Fact]
    public void ChangedRatio_is_close_to_one_when_fully_rewritten()
    {
        var source = new[] { "one", "two", "three" };
        var target = new[] { "alpha", "beta", "gamma" };
        var ops = LineDiff.Compute(source, target);
        var ratio = LineDiff.ChangedRatio(ops, source.Length);
        Assert.True(ratio >= 1.0, $"expected full rewrite ratio >= 1.0, got {ratio}");
    }

    [Fact]
    public void ChangedRatio_is_small_for_a_single_word_edit_in_a_long_document()
    {
        var source = Enumerable.Range(0, 100).Select(i => $"line {i}").ToArray();
        var target = (string[])source.Clone();
        target[50] = "line 50 EDITED";

        var ops = LineDiff.Compute(source, target);
        var ratio = LineDiff.ChangedRatio(ops, source.Length);
        Assert.True(ratio < 0.1, $"expected a tiny ratio for a one-line edit, got {ratio}");
    }

    [Fact]
    public void Insert_only_diff_does_not_duplicate_equal_line_content()
    {
        var source = new[] { "a", "b" };
        var target = new[] { "a", "b", "c" };
        var ops = LineDiff.Compute(source, target);

        foreach (var op in ops.Where(o => o.Type != DiffOpType.Insert))
            Assert.Null(op.Lines);
    }
}
