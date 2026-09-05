using AgentProfesor.Core;

namespace AgentProfesor;

/// <summary>
/// The "how does versioning actually work" window: a list of every captured version of one
/// document (keyframe vs. diff, what triggered it, how big it was) plus that version's full
/// text and a line-by-line diff against the version right before it.
/// </summary>
public sealed class VersionHistoryForm : Form
{
    private readonly VersionStore _store;
    private readonly long _documentId;
    private readonly ListView _versionsList;
    private readonly TextBox _fullTextView;
    private readonly RichTextBox _diffView;

    public VersionHistoryForm(VersionStore store, long documentId, string title)
    {
        _store = store;
        _documentId = documentId;

        Text = $"Historie verzí – {title}";
        Width = 920;
        Height = 600;
        StartPosition = FormStartPosition.CenterScreen;

        _versionsList = new ListView
        {
            Dock = DockStyle.Left,
            Width = 340,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
        };
        _versionsList.Columns.Add("Kdy", 130);
        _versionsList.Columns.Add("Typ", 70);
        _versionsList.Columns.Add("Důvod", 90);
        _versionsList.Columns.Add("Znaků", 60);
        _versionsList.SelectedIndexChanged += (_, _) => ShowSelectedVersion();

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var fullTextTab = new TabPage("Plný text verze");
        _fullTextView = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
        };
        fullTextTab.Controls.Add(_fullTextView);

        var diffTab = new TabPage("Rozdíl oproti předchozí verzi");
        _diffView = new RichTextBox
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Font = new Font(FontFamily.GenericMonospace, 9.5f),
            WordWrap = false,
        };
        diffTab.Controls.Add(_diffView);

        tabs.TabPages.Add(fullTextTab);
        tabs.TabPages.Add(diffTab);

        Controls.Add(tabs);
        Controls.Add(_versionsList);

        LoadVersions();
    }

    public void SelectVersion(long versionId)
    {
        foreach (ListViewItem item in _versionsList.Items)
        {
            if ((long)item.Tag! != versionId)
                continue;
            item.Selected = true;
            item.EnsureVisible();
            break;
        }
    }

    private void LoadVersions()
    {
        _versionsList.Items.Clear();

        foreach (var v in _store.ListVersions(_documentId))
        {
            var item = new ListViewItem(v.CapturedAt.LocalDateTime.ToString("dd.MM. HH:mm:ss"));
            item.SubItems.Add(v.IsKeyframe ? "Keyframe" : "Diff");
            item.SubItems.Add(TriggerLabel(v.Trigger));
            item.SubItems.Add(v.CharCount.ToString());
            item.Tag = v.Id;
            _versionsList.Items.Add(item);
        }

        if (_versionsList.Items.Count > 0)
            _versionsList.Items[^1].Selected = true;
    }

    private static string TriggerLabel(CaptureTrigger trigger) => trigger switch
    {
        CaptureTrigger.Pause => "pauza v psaní",
        CaptureTrigger.Periodic => "průběžně",
        CaptureTrigger.Switch => "přepnutí okna",
        CaptureTrigger.Paste => "vložení textu",
        CaptureTrigger.Shutdown => "ukončení appky",
        _ => trigger.ToString(),
    };

    private void ShowSelectedVersion()
    {
        if (_versionsList.SelectedItems.Count == 0)
            return;

        var versionId = (long)_versionsList.SelectedItems[0].Tag!;
        _fullTextView.Text = _store.GetVersionText(versionId);
        RenderDiffAgainstPrevious(versionId);
    }

    private void RenderDiffAgainstPrevious(long versionId)
    {
        _diffView.Clear();

        var items = _versionsList.Items.Cast<ListViewItem>().ToList();
        var index = items.FindIndex(i => (long)i.Tag! == versionId);
        if (index <= 0)
        {
            AppendLine(_diffView, "(první zachycená verze tohoto dokumentu – není s čím porovnat)", Color.Gray);
            return;
        }

        var previousId = (long)items[index - 1].Tag!;
        var previousLines = _store.GetVersionText(previousId).Split('\n');
        var currentLines = _store.GetVersionText(versionId).Split('\n');
        var ops = LineDiff.Compute(previousLines, currentLines);
        var cursor = 0;

        foreach (var op in ops)
        {
            switch (op.Type)
            {
                case DiffOpType.Equal:
                    for (var i = 0; i < op.Count; i++)
                        AppendLine(_diffView, "  " + previousLines[cursor + i], Color.Gray);
                    cursor += op.Count;
                    break;
                case DiffOpType.Delete:
                    for (var i = 0; i < op.Count; i++)
                        AppendLine(_diffView, "- " + previousLines[cursor + i], Color.Firebrick);
                    cursor += op.Count;
                    break;
                case DiffOpType.Insert:
                    foreach (var line in op.Lines!)
                        AppendLine(_diffView, "+ " + line, Color.DarkGreen);
                    break;
            }
        }
    }

    private static void AppendLine(RichTextBox box, string text, Color color)
    {
        box.SelectionStart = box.TextLength;
        box.SelectionLength = 0;
        box.SelectionColor = color;
        box.AppendText(text + Environment.NewLine);
    }
}
