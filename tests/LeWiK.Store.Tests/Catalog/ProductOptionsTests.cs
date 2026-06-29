using LeWiK.Store.App.Catalog.Domain;
using Xunit;

namespace LeWiK.Store.Tests.Catalog;

public class ProductOptionsTests
{
    private static OptionDraft Carta(params string[] v) => new("Carta", v);
    private static OptionDraft Idioma(params string[] v) => new("Idioma", v);
    private static VariantDraft V(string sku, string carta, string idioma) =>
        new(sku, 10000, "CLP", new Dictionary<string, string> { ["Carta"] = carta, ["Idioma"] = idioma });

    [Fact]
    public void Creates_sparse_matrix_with_valid_variants()
    {
        var result = Product.CreateWithOptions(Guid.NewGuid(), "Blister", null,
            [Carta("Charizard", "Meganium"), Idioma("Inglés", "Español")],
            [V("CHAR-EN", "Charizard", "Inglés"),
             V("CHAR-ES", "Charizard", "Español"),
             V("MEGA-EN", "Meganium", "Inglés")]); // Meganium/Español omitido a propósito

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Variants.Count);
        Assert.Equal(2, result.Value.Options.Count);
        // cada variante linkea un value por eje
        Assert.All(result.Value.Variants, v => Assert.Equal(2, v.OptionValues.Count));
    }

    [Fact]
    public void Derives_label_from_selections()
    {
        var result = Product.CreateWithOptions(Guid.NewGuid(), "Blister", null,
            [Carta("Charizard"), Idioma("Inglés")],
            [V("CHAR-EN", "Charizard", "Inglés")]);

        Assert.Equal("Charizard / Inglés", result.Value.Variants.First().Label);
    }

    [Fact]
    public void Rejects_variant_missing_an_axis()
    {
        var result = Product.CreateWithOptions(Guid.NewGuid(), "Blister", null,
            [Carta("Charizard"), Idioma("Inglés")],
            [new("BAD", 1, "CLP", new Dictionary<string, string> { ["Carta"] = "Charizard" })]); // falta Idioma

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.incomplete_selection", result.Error.Code);
    }

    [Fact]
    public void Rejects_unknown_selection_value()
    {
        var result = Product.CreateWithOptions(Guid.NewGuid(), "Blister", null,
            [Carta("Charizard"), Idioma("Inglés")],
            [V("BAD", "Pikachu", "Inglés")]); // Pikachu no es un value del eje

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.unknown_selection", result.Error.Code);
    }

    [Fact]
    public void Rejects_duplicate_combination()
    {
        var result = Product.CreateWithOptions(Guid.NewGuid(), "Blister", null,
            [Carta("Charizard"), Idioma("Inglés")],
            [V("A", "Charizard", "Inglés"), V("B", "Charizard", "Inglés")]); // misma celda

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.duplicate_combination", result.Error.Code);
    }

    [Fact]
    public void Rejects_duplicate_option_values()
    {
        var result = Product.CreateWithOptions(Guid.NewGuid(), "Blister", null,
            [Carta("Charizard", "Charizard"), Idioma("Inglés")],
            [V("A", "Charizard", "Inglés")]);

        Assert.True(result.IsFailure);
        Assert.Equal("catalog.duplicate_option_value", result.Error.Code);
    }
}