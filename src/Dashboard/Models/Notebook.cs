namespace RRSOS.PCC.Dashboard
{
    public sealed class Note
    {
        public int Id { get; set; }
        public string Text { get; set; } = "";
        public DateTime Created { get; set; }
        public int Priority { get; set; }
    }

    public sealed class Notebook
    {
        public List<Note> Notes { get; set; } = new();
    }
}
