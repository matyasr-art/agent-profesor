using AgentProfesor.Core;

namespace AgentProfesor;

/// <summary>The Ctrl+Alt+Space search window.</summary>
public sealed class SearchForm : Form
{
    private readonly VersionStore _store;
    private readonly TextBox _queryBox;
    private readonly ListView _resultsList;
    private readonly System.Windows.Forms.Timer _debounce;

    public SearchForm(VersionStore store)
    {
        _store = store;

        Text = "AgentProfesor – hledání";
        Width = 780;
        Height = 480;
        StartPosition = FormStartPosition.CenterScreen;

        _resultsList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
        };
        _resultsList.Columns.Add("Aplikace", 110);
        _resultsList.Columns.Add("Okno / dokument", 220);
        _resultsList.Columns.Add("Kdy", 130);
        _resultsList.Columns.Add("Úryvek", 280);
        _resultsList.DoubleClick += (_, _) => OpenHistory();
        _resultsList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
                OpenHistory();
        };

        _queryBox = new TextBox { Dock = DockStyle.Top, Font = new Font(FontFamily.GenericSansSerif, 12f) };
        _queryBox.TextChanged += (_, _) => RestartDebounce();
        _queryBox.KeyDown += (_, e) =>
        {
            switch (e.KeyCode)
            {
                case Keys.Enter:
                    RunSearch();
                    e.SuppressKeyPress = true;
                    break;
                case Keys.Escape:
                    Close();
                    break;
                case Keys.Down:
                    if (_resultsList.Items.Count > 0)
                    {
                        _resultsList.Focus();
                        _resultsList.Items[0].Selected = true;
                    }
                    break;
            }
        };

        _debounce = new System.Windows.Forms.Timer { Interval = 200 };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            RunSearch();
        };

        Controls.Add(_resultsList);
        Controls.Add(_queryBox);

        Shown += (_, _) => _queryBox.Focus();
    }

    private void RestartDebounce()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void RunSearch()
    {
        _resultsList.Items.Clear();

        var query = _queryBox.Text.Trim();
        if (query.Length == 0)
            return;

        IReadOnlyList<SearchHit> hits;
        try
        {
            hits = _store.Search(query);
        }
        catch
        {
            return;
        }

        foreach (var hit in hits)
        {
            var item = new ListViewItem(AppNames.ToFriendly(hit.AppName));
            item.SubItems.Add(hit.WindowTitle);
            item.SubItems.Add(hit.CapturedAt.LocalDateTime.ToString("dd.MM. HH:mm"));
            item.SubItems.Add(hit.Snippet);
            item.Tag = hit;
            _resultsList.Items.Add(item);
        }
    }

    private void OpenHistory()
    {
        if (_resultsList.SelectedItems.Count == 0)
            return;

        var hit = (SearchHit)_resultsList.SelectedItems[0].Tag!;
        var history = new VersionHistoryForm(_store, hit.DocumentId, hit.WindowTitle);
        history.Show();
        history.SelectVersion(hit.VersionId);
    }
}
