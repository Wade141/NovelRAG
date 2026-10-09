namespace NovelRAG.Desktop;

public sealed class IdeaEditor : Grid
{
    public TextBox ContentInput { get; } = Ui.Input("IdeaContent", true);
    public ComboBox ProjectInput { get; } = new() { Margin = new Thickness(0, 4, 0, 10), Padding = new Thickness(8), DisplayMemberPath = "Name", SelectedValuePath = "Id" };
    public TextBox TagsInput { get; } = Ui.Input("IdeaTags");
    private string baseline = "";
    private string? type;
    public bool Ready { get; private set; }
    public bool Dirty => Ready && Signature() != baseline;
    public IdeaEditor(Func<Task>? createProject = null)
    {
        IsEnabled = false;
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto });
        ContentInput.MinHeight = 130; ContentInput.VerticalContentAlignment = VerticalAlignment.Top; Children.Add(ContentInput);
        var projectPanel = new StackPanel(); projectPanel.Children.Add(Ui.Text("作品")); projectPanel.Children.Add(ProjectInput);
        if (createProject is not null) projectPanel.Children.Add(Ui.Row(Ui.Button("新建", createProject)));
        Grid.SetRow(projectPanel, 1); Children.Add(projectPanel);
        var tagsPanel = new StackPanel(); tagsPanel.Children.Add(Ui.Text("标签 · 以逗号分隔，可自由创建", 12, Ui.Muted)); tagsPanel.Children.Add(TagsInput); Grid.SetRow(tagsPanel, 2); Children.Add(tagsPanel);
    }
    private string Signature() => ContentInput.Text + "\u001f" + ProjectInput.SelectedValue + "\u001f" + TagsInput.Text;
    public IdeaDraft Draft() => new(ContentInput.Text, ProjectInput.SelectedValue is long id && id > 0 ? id : null, Ui.ParseTags(TagsInput.Text), type);
    public async Task LoadAsync(AppServices services, Idea? idea = null, long? preferredProject = null)
    {
        Ready = false; IsEnabled = false;
        type = idea?.Type;
        ProjectInput.ItemsSource = new[] { new Project(0, "未分类") }.Concat(await services.Projects.ListAsync()).ToArray();
        var id = idea?.ProjectId ?? preferredProject ?? 0;
        ProjectInput.SelectedValue = id;
        if (ProjectInput.SelectedIndex < 0) ProjectInput.SelectedIndex = 0;
        ContentInput.Text = idea?.Content ?? ""; TagsInput.Text = idea?.TagText ?? "";
        MarkClean(); Ready = true; IsEnabled = true;
    }
    public void MarkClean() => baseline = Signature();
}
