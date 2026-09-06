using AgentProfesor.Core;

namespace AgentProfesor;

/// <summary>
/// Uživatelské okno historie: seznam verzí jednoho dokumentu (kdy a co se dělo), celý text
/// vybrané verze a barevně odlišené „co se změnilo" oproti předchozí verzi. Vědomě bez
/// vývojářského žargonu (keyframe/diff) – cílový uživatel je expert ve svém oboru, ale ne v IT
/// a nebude nic nastavovat ani řešit interní pojmy.
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
        // Vědomě žádný „Keyframe/Diff" sloupec – to je interní vývojářský detail úložiště, který
        // uživateli (ne-technik) nic neřekne. Vidí jen: kdy a co se dělo.
        _versionsList.Columns.Add("Kdy", 160);
        _versionsList.Columns.Add("Co se dělo", 170);
        _versionsList.SelectedIndexChanged += (_, _) => ShowSelectedVersion();

        var tabs = new TabControl { Dock = DockStyle.Fill };

        var fullTextTab = new TabPage("Celý text");
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

        var diffTab = new TabPage("Co se změnilo");
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
            var item = new ListViewItem(v.CapturedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm"));
            item.SubItems.Add(TriggerLabel(v.Trigger));
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
