using System;
using Godot;
using Mapmaker.Core;

namespace Mapmaker.GodotApp;

public partial class MapView
{
    private ImageTexture? _humidityTexture, _humidityRows;
    private float[]? _boundHumidity;

    private void BindHumidity()
    {
        var world = _world;
        var wind = world?.ClimateWind;
        var humidity = world?.ClimateHumidity;
        _material.SetShaderParameter("has_humidity", humidity is not null && wind is not null);
        if (humidity is null || wind is null || ReferenceEquals(humidity, _boundHumidity)) return;
        _humidityTexture?.Dispose(); _humidityRows?.Dispose();
        var values = new byte[humidity.Length * sizeof(float)];
        Buffer.BlockCopy(humidity, 0, values, 0, values.Length);
        using var image = Image.CreateFromData(wind.GridWidth, wind.GridHeight, false, Image.Format.Rf, values);
        _humidityTexture = ImageTexture.CreateFromImage(image);
        var rows = new float[world!.Height * 2];
        for (int y = 0; y < world.Height; y++)
        {
            var (left, right) = world.Domain.RowBounds(y);
            rows[y * 2] = left; rows[y * 2 + 1] = right - left;
        }
        var rowBytes = new byte[rows.Length * sizeof(float)];
        Buffer.BlockCopy(rows, 0, rowBytes, 0, rowBytes.Length);
        using var rowImage = Image.CreateFromData(1, world.Height, false, Image.Format.Rgf, rowBytes);
        _humidityRows = ImageTexture.CreateFromImage(rowImage);
        _material.SetShaderParameter("humidity_data", _humidityTexture);
        _material.SetShaderParameter("humidity_rows", _humidityRows);
        _material.SetShaderParameter("humidity_wrap", wind.WrapHorizontal);
        _boundHumidity = humidity;
    }

    private void RefreshHumidityInspection(WorldData world, Vector2I offset)
    {
        RefreshHadleyWind();
        if (world.ClimateHumidity is not float[] humidity || world.ClimateWind is not HadleyWindField wind)
        {
            _inspection.Text = _hadleyError is null ? "Calculando umidade do ar…" : "Não foi possível calcular a umidade.";
            _inspection.TooltipText = _hadleyError ?? "A umidade será reconstruída a partir da receita deste mapa.";
            return;
        }
        const string legend = "Umidade relativa do ar: vermelho 0% · claro 50% · azul 100%";
        if (offset.X < 0) _inspection.Text = legend;
        else
        {
            var (left, right) = world.Domain.RowBounds(offset.Y);
            float x = (offset.X - left + 0.5f) / (right - left) * wind.GridWidth - 0.5f;
            float y = (offset.Y + 0.5f) / world.Height * wind.GridHeight - 0.5f;
            float value = HadleyWindSimulation.SampleGrid(humidity, wind.GridWidth, wind.GridHeight, wind.WrapHorizontal, x, y);
            _inspection.Text = $"Hex {offset.X}, {offset.Y} · Umidade relativa {value * 100:0.0}% · {legend}";
        }
        _inspection.TooltipText = "Média da umidade relativa na camada baixa durante a simulação. Chuva é água precipitada em mm/ano; esta camada mostra vapor em relação à capacidade do ar, de 0 a 100%.";
    }
}
