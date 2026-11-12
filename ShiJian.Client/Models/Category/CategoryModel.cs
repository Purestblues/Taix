namespace ShiJian.Client.Models.Category;

public class CategoryModel : CategoryCardModel
{
    public Shared.Models.CategoryModel Data { get; set; }

    public override string? Name => Data?.Name;

    public override string? IconFile => Data?.IconFile;

    protected override string? RawColor => Data?.Color;

    protected override bool IsSystemCategory => Data?.IsSystem ?? false;
}
