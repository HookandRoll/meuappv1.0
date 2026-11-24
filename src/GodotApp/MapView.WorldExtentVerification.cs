using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private void VerifyExtentCreationForm()
    {
        var form = new WorldCreationDialog();
        AddChild(form);
        try
        {
            WorldManifest? captured = null;
            form.CreateRequested = (manifest, _) => captured = manifest;
            form.ShowForm();
            var choice = form.FindChild("MapExtent", recursive: true, owned: false) as OptionButton;
            Require(choice is not null && choice.Selected == (int)MapExtent.World, "Novo mapa deve oferecer Mundo como padrão.");
            form.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            Require(captured?.Generation.Extent == MapExtent.World && captured.GeneratorVersion == WorldCreationDialog.ClimateRecipe,
                "Criar Mundo perdeu a opção de oceano periférico.");
            WorldManifest world = captured!;
            var expected = MapDomain.ExpandedSize(256, 128);
            Require(world.Generation.PolarOceanRows == 0 && world.Width == expected.Width && world.Height == expected.Height &&
                world.Generation.ReferenceWidth == 256 && world.Generation.ReferenceHeight == 128,
                "Mundo deve gerar a área ampliada inteira, sem faixas copiadas.");
            var sizeChoice = form.FindChild("MapSize", recursive: true, owned: false) as OptionButton;
            var warning = form.FindChild("WorldCapacityWarning", recursive: true, owned: false) as Label;
            form.ShowForm();
            sizeChoice!.Select(5); sizeChoice.EmitSignal(OptionButton.SignalName.ItemSelected, 5L);
            Require(warning is not null && warning.Visible && warning.Text.Contains("4000 × 2000"),
                "Atlas grande deve avisar que supera 4000 × 2000 hexes.");
            form.ShowForm();
            choice!.Select((int)MapExtent.Region);
            choice.EmitSignal(OptionButton.SignalName.ItemSelected, (long)MapExtent.Region);
            form.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            Require(captured?.Generation.Extent == MapExtent.Region, "Criar Região perdeu o recorte aberto.");
            form.ShowForm(captured);
            Require(choice.Selected == (int)MapExtent.Region, "Gerar terreno perdeu o tipo Região existente.");
            form.ShowForm(world);
            Require(choice.Selected == (int)MapExtent.World, "Gerar terreno perdeu o tipo Mundo existente.");
            GD.Print("WORLD EXTENT FORM PASSED: default World, Region selection, manifest and existing-map restoration.");
        }
        finally
        {
            form.Hide();
            form.QueueFree();
        }
    }
}
