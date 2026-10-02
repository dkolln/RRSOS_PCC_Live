namespace RRSOS.PCC.Dashboard
{
    public sealed class ShoppingIngredient
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Count { get; set; } = 1;
    }

    /// <summary>One thing the player pinned in the game, with its recipe as it was when pinned and how many of it are wanted.</summary>
    public sealed class ShoppingEntry
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int Qty { get; set; } = 1;
        public DateTime Added { get; set; }
        public List<ShoppingIngredient> Ingredients { get; set; } = new();
    }

    public sealed class ShoppingFile
    {
        public List<ShoppingEntry> Items { get; set; } = new();
    }
}
