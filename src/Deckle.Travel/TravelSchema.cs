using System.Text.Json.Nodes;
using Deckle.Anytype;

namespace Deckle.Travel;

// The public managed schema contract of the Travel space. Keys are the stable
// English norm; every French label is resolved through the terms file. There
// is no code grammar: trips identify by destination and dates, objects by
// name and links. The Date is the state of an Activity — deliberately no
// status property anywhere in this contract.
public static class TravelSchema
{
    public static class Types
    {
        public const string Stay = "stay";
        public const string Stage = "stage";
        public const string Place = "place";
        public const string Activity = "activity";
        public const string Transfer = "transfer";
        public const string Lodging = "lodging";
        public const string Expense = "expense";
    }

    public static class Properties
    {
        public const string StartDate = "start_date";
        public const string EndDate = "end_date";
        public const string Date = "date";
        public const string Appointment = "appointment";
        public const string Arrival = "arrival";
        public const string Departure = "departure";
        public const string Duration = "duration";
        public const string VisitDuration = "visit_duration";
        public const string Accessibility = "accessibility";
        public const string Address = "address";
        public const string OfficialSite = "official_site";
        public const string Confirmation = "confirmation";
        public const string Files = "files";
        public const string Amount = "amount";
        public const string ActivityCategory = "activity_category";
        public const string ExpenseCategory = "expense_category";
        public const string PlaceCategory = "place_category";
        public const string Mode = "mode";
        public const string Stay = "stay";
        public const string Stage = "stage";
        public const string Place = "place";
        public const string Expense = "expense";
    }

    public static class ActivityCategories
    {
        public const string Walk = "walk";
        public const string Visit = "visit";
        public const string Evening = "evening";
        public const string Sport = "sport";
        public const string Meal = "meal";
        public const string Other = "other";
    }

    public static class ExpenseCategories
    {
        public const string Transport = "transport";
        public const string Lodging = "lodging";
        public const string Food = "food";
        public const string Activity = "activity";
        public const string Purchase = "purchase";
        public const string Fees = "fees";
        public const string Other = "other";
    }

    public static class TransferModes
    {
        public const string Plane = "plane";
        public const string Train = "train";
        public const string Bus = "bus";
        public const string Ferry = "ferry";
        public const string Car = "car";
    }

    public static readonly IReadOnlyList<string> CreatableTypes =
    [
        Types.Stay, Types.Stage, Types.Place, Types.Activity,
        Types.Transfer, Types.Lodging, Types.Expense,
    ];

    internal static readonly IReadOnlyDictionary<string, string> RequiredProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Properties.StartDate] = "date",
            [Properties.EndDate] = "date",
            [Properties.Date] = "date",
            [Properties.Appointment] = "date",
            [Properties.Arrival] = "date",
            [Properties.Departure] = "date",
            [Properties.Duration] = "text",
            [Properties.VisitDuration] = "text",
            [Properties.Accessibility] = "text",
            [Properties.Address] = "text",
            [Properties.OfficialSite] = "url",
            [Properties.Confirmation] = "text",
            [Properties.Files] = "files",
            [Properties.Amount] = "number",
            [Properties.ActivityCategory] = "select",
            [Properties.ExpenseCategory] = "select",
            [Properties.PlaceCategory] = "select",
            [Properties.Mode] = "select",
            [Properties.Stay] = "objects",
            [Properties.Stage] = "objects",
            [Properties.Place] = "objects",
            [Properties.Expense] = "objects",
        };

    internal static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> RequiredByType =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [Types.Stay] = [Properties.StartDate, Properties.EndDate],
            [Types.Stage] = [Properties.Stay, Properties.StartDate, Properties.EndDate],
            [Types.Place] =
            [
                Properties.PlaceCategory, Properties.Accessibility, Properties.VisitDuration,
                Properties.Address, Properties.OfficialSite,
            ],
            [Types.Activity] =
            [
                Properties.ActivityCategory, Properties.Date, Properties.Appointment,
                Properties.Duration, Properties.Place, Properties.Files,
                Properties.Expense, Properties.Stay,
            ],
            [Types.Transfer] =
            [
                Properties.Date, Properties.Appointment, Properties.Mode,
                Properties.Confirmation, Properties.Files, Properties.Expense, Properties.Stay,
            ],
            [Types.Lodging] =
            [
                Properties.Stage, Properties.Arrival, Properties.Departure,
                Properties.Address, Properties.Confirmation, Properties.Files, Properties.Expense,
            ],
            [Types.Expense] =
            [
                Properties.Amount, Properties.Date, Properties.ExpenseCategory, Properties.Stay,
            ],
        };

    // Closed vocabularies ship their full option set; options are added by the
    // user in Anytype, never by the surface. The Place category is a live
    // select on purpose — its options belong to the user from day one, so it
    // carries no compiled vocabulary here.
    internal static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ClosedVocabularies =
        BuildClosedVocabularies();

    internal static JsonObject CreateRequiredSchemaManifest()
    {
        var properties = new JsonArray();
        foreach ((string key, string format) in RequiredProperties)
        {
            var property = new JsonObject
            {
                ["key"] = key,
                ["name"] = TravelTerms.Current.PropertyName(key),
                ["format"] = format,
            };
            if (ClosedVocabularies.TryGetValue(key, out var vocabulary))
            {
                var tags = new JsonArray();
                foreach ((string tagKey, string name) in vocabulary)
                    tags.Add(new JsonObject { ["key"] = tagKey, ["name"] = name });
                property["tags"] = tags;
            }
            properties.Add(property);
        }

        var types = new JsonArray();
        foreach (string type in CreatableTypes)
        {
            var attached = new JsonArray();
            foreach (string property in RequiredByType[type]) attached.Add(property);
            types.Add(new JsonObject
            {
                ["key"] = type,
                ["name"] = TravelTerms.Current.TypeName(type),
                ["plural_name"] = TravelTerms.Current.TypePluralName(type),
                ["layout"] = "basic",
                ["properties"] = attached,
            });
        }

        return new JsonObject { ["types"] = types, ["properties"] = properties };
    }

    internal static TravelSchemaRuntime Validate(SchemaSnapshot snapshot)
    {
        var failures = new List<string>();

        foreach ((string key, string expectedFormat) in RequiredProperties)
        {
            if (!snapshot.Properties.TryGetValue(key, out SchemaPropertyInfo? property))
                failures.Add($"propriété manquante {key}");
            else if (!string.Equals(property.Format, expectedFormat, StringComparison.Ordinal))
                failures.Add($"propriété {key} : format {property.Format}, attendu {expectedFormat}");
        }

        foreach ((string typeKey, IReadOnlyList<string> requiredProperties) in RequiredByType)
        {
            if (!snapshot.Types.TryGetValue(typeKey, out SchemaTypeInfo? type))
            {
                failures.Add($"type manquant {typeKey}");
                continue;
            }

            foreach (string propertyKey in requiredProperties)
                if (snapshot.Properties.TryGetValue(propertyKey, out SchemaPropertyInfo? property)
                    && !type.PropertyLinks.Any(link => LinkMatches(link, property)))
                {
                    failures.Add($"type {typeKey} : propriété non attachée {propertyKey}");
                }
        }

        foreach ((string propertyKey, IReadOnlyDictionary<string, string> vocabulary) in ClosedVocabularies)
        {
            snapshot.TagsByProperty.TryGetValue(propertyKey, out var tags);
            tags ??= new Dictionary<string, SchemaTagInfo>(StringComparer.Ordinal);
            foreach ((string key, string name) in vocabulary)
                if (!tags.Values.Distinct().Any(tag =>
                        string.Equals(tag.Key, key, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(tag.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add($"vocabulaire {propertyKey} : option manquante {key}");
                }
        }

        if (failures.Count > 0)
            throw new TravelSchemaException(
                "Le schéma Travel n’est pas conforme : " + string.Join(" ; ", failures)
                + ". Applique le manifeste Travel avec schema-admin puis réessaie.");

        return new TravelSchemaRuntime(snapshot);
    }

    private static bool LinkMatches(SchemaPropertyLinkInfo link, SchemaPropertyInfo property) =>
        (link.Key.Length > 0 && string.Equals(link.Key, property.Key, StringComparison.Ordinal))
        || (link.Id.Length > 0 && string.Equals(link.Id, property.Id, StringComparison.Ordinal));

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> BuildClosedVocabularies()
    {
        return new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [Properties.ActivityCategory] = Vocabulary(Properties.ActivityCategory,
            [
                ActivityCategories.Walk, ActivityCategories.Visit, ActivityCategories.Evening,
                ActivityCategories.Sport, ActivityCategories.Meal, ActivityCategories.Other,
            ]),
            [Properties.ExpenseCategory] = Vocabulary(Properties.ExpenseCategory,
            [
                ExpenseCategories.Transport, ExpenseCategories.Lodging, ExpenseCategories.Food,
                ExpenseCategories.Activity, ExpenseCategories.Purchase, ExpenseCategories.Fees,
                ExpenseCategories.Other,
            ]),
            [Properties.Mode] = Vocabulary(Properties.Mode,
            [
                TransferModes.Plane, TransferModes.Train, TransferModes.Bus,
                TransferModes.Ferry, TransferModes.Car,
            ]),
        };

        static IReadOnlyDictionary<string, string> Vocabulary(
            string propertyKey, IReadOnlyList<string> optionKeys)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string key in optionKeys)
                result[key] = TravelTerms.Current.OptionName(propertyKey, key);
            return result;
        }
    }
}

public sealed class TravelSchemaException(string message) : InvalidOperationException(message);
