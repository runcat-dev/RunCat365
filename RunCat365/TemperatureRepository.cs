// Copyright 2025 Takuto Nakamura
//
//    Licensed under the Apache License, Version 2.0 (the "License");
//    you may not use this file except in compliance with the License.
//    You may obtain a copy of the License at
//
//        http://www.apache.org/licenses/LICENSE-2.0
//
//    Unless required by applicable law or agreed to in writing, software
//    distributed under the License is distributed on an "AS IS" BASIS,
//    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//    See the License for the specific language governing permissions and
//    limitations under the License.

using RunCat365.Properties;
using System.Globalization;

namespace RunCat365
{
    struct TemperatureInfo
    {
        internal float AverageCelsius { get; set; }
        internal float MaximumCelsius { get; set; }
    }

    internal static class TemperatureInfoExtension
    {
        private const float CELSIUS_TO_FAHRENHEIT_SCALE = 9.0f / 5.0f;
        private const float CELSIUS_TO_FAHRENHEIT_OFFSET = 32.0f;

        internal static string GetDescription(this TemperatureInfo temperatureInfo, TemperatureUnit unit)
        {
            var resolvedUnit = unit.Resolve();
            return $"{Strings.SystemInfo_Temperature}: {temperatureInfo.MaximumCelsius.ToLocalizedTemperatureText(resolvedUnit)}";
        }

        internal static List<string> GenerateIndicator(this TemperatureInfo temperatureInfo, TemperatureUnit unit)
        {
            var resolvedUnit = unit.Resolve();
            return [
                TreeFormatter.CreateRoot($"{Strings.SystemInfo_Temperature}:"),
                TreeFormatter.CreateNode($"{Strings.SystemInfo_Average}: {temperatureInfo.AverageCelsius.ToLocalizedTemperatureText(resolvedUnit)}", false),
                TreeFormatter.CreateNode($"{Strings.SystemInfo_Maximum}: {temperatureInfo.MaximumCelsius.ToLocalizedTemperatureText(resolvedUnit)}", true)
            ];
        }

        private static string ToLocalizedTemperatureText(this float temperatureCelsius, TemperatureUnit resolvedUnit)
        {
            var useFahrenheit = resolvedUnit == TemperatureUnit.Fahrenheit;
            var value = useFahrenheit
                ? temperatureCelsius * CELSIUS_TO_FAHRENHEIT_SCALE + CELSIUS_TO_FAHRENHEIT_OFFSET
                : temperatureCelsius;
            var format = useFahrenheit
                ? Strings.SystemInfo_TemperatureFahrenheitFormat
                : Strings.SystemInfo_TemperatureCelsiusFormat;
            return string.Format(CultureInfo.CurrentCulture, format, value);
        }
    }

    internal sealed class TemperaturePerformanceCounters : InstancedPerformanceCounters
    {
        protected override string CategoryName => "Thermal Zone Information";
        protected override string CounterName => "Temperature";

        internal static TemperaturePerformanceCounters? TryCreate()
        {
            var instance = new TemperaturePerformanceCounters();
            return instance.TryInitialize() ? instance : null;
        }
    }

    internal class TemperatureRepository
    {
        private const float KELVIN_TO_CELSIUS_OFFSET = 273.15f;
        private const float MIN_VALID_TEMPERATURE_CELSIUS = -50.0f;
        private const float MAX_VALID_TEMPERATURE_CELSIUS = 150.0f;
        private const int REFRESH_INTERVAL_TICKS = 30;
        private const int STALE_READ_THRESHOLD = 24;

        private readonly TemperaturePerformanceCounters? counters;
        private readonly List<float> baselineRawValues = [];
        private TemperatureInfo? temperatureInfo;
        private int ticksSinceLastRefresh;
        private int identicalReadCount;
        private bool hasEverVaried;

        internal bool IsAvailable => counters is not null;

        internal TemperatureRepository()
        {
            counters = TemperaturePerformanceCounters.TryCreate();
        }

        internal void Update()
        {
            if (counters is null) return;

            ticksSinceLastRefresh += 1;
            if (REFRESH_INTERVAL_TICKS <= ticksSinceLastRefresh)
            {
                ticksSinceLastRefresh = 0;
                counters.RefreshInstances();
            }

            var rawValues = counters.ReadValues();
            TrackValueVariation(rawValues);

            var temperaturesCelsius = new List<float>(rawValues.Count);
            foreach (var temperatureKelvin in rawValues)
            {
                if (temperatureKelvin <= 0) continue;
                var temperatureCelsius = temperatureKelvin - KELVIN_TO_CELSIUS_OFFSET;
                if (temperatureCelsius is < MIN_VALID_TEMPERATURE_CELSIUS or > MAX_VALID_TEMPERATURE_CELSIUS) continue;
                temperaturesCelsius.Add(temperatureCelsius);
            }

            temperatureInfo = temperaturesCelsius.Count == 0 || IsLikelyPlaceholder()
                ? null
                : new TemperatureInfo
                {
                    AverageCelsius = temperaturesCelsius.Average(),
                    MaximumCelsius = temperaturesCelsius.Max()
                };
        }

        // Some firmware exposes an ACPI thermal zone that is not wired to a real
        // sensor and always reports the same fixed value (e.g. 301 K). Track whether
        // the readings have ever changed so such zones can be treated as unavailable
        // instead of showing a misleading constant temperature.
        private void TrackValueVariation(List<float> rawValues)
        {
            if (hasEverVaried || rawValues.Count == 0) return;

            var sortedValues = new List<float>(rawValues);
            sortedValues.Sort();

            if (baselineRawValues.Count != sortedValues.Count)
            {
                baselineRawValues.Clear();
                baselineRawValues.AddRange(sortedValues);
                identicalReadCount = 1;
                return;
            }

            for (var i = 0; i < sortedValues.Count; i++)
            {
                if (baselineRawValues[i] != sortedValues[i])
                {
                    hasEverVaried = true;
                    baselineRawValues.Clear();
                    return;
                }
            }

            identicalReadCount += 1;
        }

        private bool IsLikelyPlaceholder()
        {
            return !hasEverVaried && STALE_READ_THRESHOLD <= identicalReadCount;
        }

        internal TemperatureInfo? Get()
        {
            return temperatureInfo;
        }

        internal void Close()
        {
            counters?.Close();
        }
    }
}
