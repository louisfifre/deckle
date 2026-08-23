using System.Text.Json.Nodes;
using Deckle.Anytype;
using Xunit;

namespace Deckle.Anytype.Tests;

[Trait("Category", "integration")]
public class SchemaAdminGesturesTests
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static SchemaAdminGestures NewGestures(FakeAnytypeServer server)
    {
        var client = new AnytypeApiClient(server.Credentials);
        var aliases = new AnytypeSpaceAliases(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["home"] = FakeAnytypeServer.Space,
            });
        return new SchemaAdminGestures(client, aliases);
    }

    static JsonObject EmptyList() => new() { ["data"] = new JsonArray() };

    static JsonObject Page(JsonArray data, bool hasMore = false) => new()
    {
        ["data"] = data,
        ["pagination"] = new JsonObject { ["has_more"] = hasMore },
    };

    static JsonObject ExistingType(
        string name = "Pièce",
        string pluralName = "Pièces",
        JsonArray? properties = null,
        JsonObject? icon = null)
    {
        var type = new JsonObject
        {
            ["id"] = "type-piece",
            ["key"] = "piece",
            ["name"] = name,
            ["plural_name"] = pluralName,
            ["layout"] = "basic",
            ["properties"] = properties ?? new JsonArray(),
        };
        if (icon is not null)
            type["icon"] = icon;
        return type;
    }

    static JsonObject TypeManifest(JsonObject? icon = null)
    {
        var type = new JsonObject
        {
            ["key"] = "piece",
            ["name"] = "Pièce",
        };
        if (icon is not null)
            type["icon"] = icon;
        return new JsonObject { ["types"] = new JsonArray { type } };
    }

    static JsonObject ExistingProperty() => new()
    {
        ["id"] = "prop-etat",
        ["key"] = "etat_identification",
        ["name"] = "État d'identification",
        ["format"] = "select",
    };

    static JsonObject ExistingTag() => new()
    {
        ["id"] = "tag-confirme",
        ["name"] = "Confirmé",
        ["color"] = "grey",
    };

    static JsonObject Manifest() => new()
    {
        ["properties"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = "etat_identification",
                ["name"] = "État d'identification",
                ["format"] = "select",
                ["tags"] = new JsonArray
                {
                    new JsonObject { ["key"] = "confirme", ["name"] = "Confirmé" },
                },
            },
        },
        ["types"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = "piece",
                ["name"] = "Pièce",
                ["plural_name"] = "Pièces",
                ["layout"] = "basic",
                ["properties"] = new JsonArray { "etat_identification" },
            },
        },
    };

    [Fact]
    public async Task PreviewReportsAdditiveActionsWithoutWriting()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.Contains("create_property", digest);
        Assert.Contains("create_tag", digest);
        Assert.Contains("create_type", digest);
        Assert.Contains("attach_property", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewReturnsTheTypedPlanAndItsTextFallbackTogether()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());

        SchemaPreviewResult result = await NewGestures(server)
            .PreviewResultAsync("home", Manifest(), Ct);

        Assert.Equal("home", result.SpaceAlias);
        Assert.Equal(64, result.PreviewId.Length);
        Assert.Collection(
            result.Actions,
            action => Assert.Equal("create_property", action.Kind),
            action => Assert.Equal("create_tag", action.Kind),
            action => Assert.Equal("create_type", action.Kind),
            action => Assert.Equal("attach_property", action.Kind));
        Assert.Empty(result.Conflicts);
        Assert.Empty(result.SkippedConflicts);
        Assert.Contains($"Preview {result.PreviewId}", result.Digest);
        Assert.Contains("create_property", result.Digest);
        Assert.DoesNotContain(server.Requests, request => request.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewIdIsDeterministicAcrossGestureInstances()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());

        string first = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);
        string second = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.Equal(PreviewId(first), PreviewId(second));
    }

    [Fact]
    public async Task PreviewDoesNotReattachExistingPropertyReferencedById()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "prop-etat" }) }));
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("attach_property", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewDoesNotReattachExistingPropertyReferencedByKey()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "etat_identification" }) }));
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("attach_property", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewDoesNotRecreateExistingTagMatchedByName()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", Page(new JsonArray { ExistingTag() }));

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("create_tag", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewReadsEveryPaginatedSchemaPage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray(), hasMore: true));
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "prop-etat" }) }));
        server.OnListProperties(Page(new JsonArray(), hasMore: true));
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("create_property", digest);
        Assert.DoesNotContain("create_type", digest);
        Assert.DoesNotContain("attach_property", digest);
    }

    [Fact]
    public async Task PreviewReadsEveryPaginatedTagPage()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListPropertyTags("prop-etat", Page(new JsonArray(), hasMore: true));
        server.OnListPropertyTags("prop-etat", Page(new JsonArray { ExistingTag() }));

        string digest = await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain("create_tag", digest);
    }

    [Fact]
    public async Task PreviewRejectsDuplicateTypeKeysBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["types"] = new JsonArray
            {
                new JsonObject { ["key"] = "piece", ["name"] = "Pièce" },
                new JsonObject { ["key"] = "piece", ["name"] = "Autre pièce" },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("Doublon", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewRejectsUnknownManifestFieldsBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["properties"] = new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "etat_identification",
                    ["name"] = "État d'identification",
                    ["format"] = "select",
                    ["unexpected"] = true,
                },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("Champ inconnu", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewRejectsAnytypeLayoutsThatCannotCreateTypes()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["types"] = new JsonArray
            {
                new JsonObject { ["key"] = "piece", ["name"] = "Pièce", ["layout"] = "page" },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("Layout inconnu", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewAcceptsCollectionLayoutSupportedByTheAnytypeApi()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        JsonObject manifest = new()
        {
            ["types"] = new JsonArray
            {
                new JsonObject
                {
                    ["key"] = "niveau",
                    ["name"] = "Niveau",
                    ["layout"] = "collection",
                },
            },
        };

        string digest = await NewGestures(server).PreviewAsync("home", manifest, Ct);

        Assert.Contains("create_type · niveau", digest);
        Assert.DoesNotContain(server.Requests, request => request.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewKeepsTypeIconOptional()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", TypeManifest(), Ct);

        Assert.Contains("create_type · piece", digest);
        Assert.DoesNotContain("set_icon", digest);
    }

    [Fact]
    public async Task PreviewParsesBuiltInAndEmojiIcons()
    {
        using var builtInServer = new FakeAnytypeServer();
        builtInServer.OnListTypes(EmptyList());
        builtInServer.OnListProperties(EmptyList());
        string builtIn = await NewGestures(builtInServer).PreviewAsync(
            "home",
            TypeManifest(new JsonObject
            {
                ["format"] = "icon",
                ["name"] = "home",
                ["color"] = "blue",
            }),
            Ct);

        using var emojiServer = new FakeAnytypeServer();
        emojiServer.OnListTypes(EmptyList());
        emojiServer.OnListProperties(EmptyList());
        string emoji = await NewGestures(emojiServer).PreviewAsync(
            "home",
            TypeManifest(new JsonObject { ["format"] = "emoji", ["emoji"] = "🚪" }),
            Ct);

        Assert.Contains("set_icon · piece · icon:home:blue", builtIn);
        Assert.Contains("set_icon · piece · emoji:🚪", emoji);
    }

    [Fact]
    public async Task PreviewRejectsUnknownBuiltInIconNameBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            NewGestures(server).PreviewAsync(
                "home",
                TypeManifest(new JsonObject { ["format"] = "icon", ["name"] = "front-door" }),
                Ct));

        Assert.Contains("Nom d’icône Anytype inconnu", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewRejectsUnknownIconColorBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            NewGestures(server).PreviewAsync(
                "home",
                TypeManifest(new JsonObject
                {
                    ["format"] = "icon",
                    ["name"] = "home",
                    ["color"] = "green",
                }),
                Ct));

        Assert.Contains("Couleur d’icône Anytype inconnue", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewReportsExistingIconAsSkippedConflict()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray
        {
            ExistingType(icon: new JsonObject
            {
                ["format"] = "icon",
                ["name"] = "home",
                ["color"] = "grey",
            }),
        }));
        server.OnListProperties(EmptyList());

        string digest = await NewGestures(server).PreviewAsync(
            "home",
            TypeManifest(new JsonObject { ["format"] = "icon", ["name"] = "bed" }),
            Ct);

        Assert.Contains("Conflits ignorés (additif seulement)", digest);
        Assert.Contains("icône existante icon:home:grey, demandée icon:bed", digest);
        Assert.DoesNotContain("Actions additives :\n- set_icon", digest);
        Assert.DoesNotContain(server.Requests, request => request.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewStaysSilentWhenTheExistingIconIsTheRequestedOne()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray
        {
            ExistingType(icon: new JsonObject
            {
                ["format"] = "icon",
                ["name"] = "home",
                ["color"] = "blue",
            }),
        }));
        server.OnListProperties(EmptyList());

        string digest = await NewGestures(server).PreviewAsync(
            "home",
            TypeManifest(new JsonObject
            {
                ["format"] = "icon",
                ["name"] = "home",
                ["color"] = "blue",
            }),
            Ct);

        Assert.Contains("Aucune création additive nécessaire.", digest);
        Assert.DoesNotContain("Conflits ignorés", digest);
    }

    [Fact]
    public async Task InspectIncludesCurrentTypeIcon()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray
        {
            ExistingType(icon: new JsonObject { ["format"] = "emoji", ["emoji"] = "🚪" }),
        }));
        server.OnListProperties(EmptyList());

        string digest = await NewGestures(server).InspectAsync("home", Ct);

        Assert.Contains("piece · Pièce · basic · emoji:🚪", digest);
    }

    [Fact]
    public async Task PreviewRejectsNonStringTypeLayoutBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["types"] = new JsonArray
            {
                new JsonObject { ["key"] = "piece", ["name"] = "Pièce", ["layout"] = 42 },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("doit être une string", ex.Message);
        Assert.Empty(server.Requests);
    }


    [Fact]
    public async Task ApplyRequiresConfirmTrueBeforeLookingUpPreview()
    {
        using var server = new FakeAnytypeServer();

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => NewGestures(server).ApplyAsync(
                "home", "deadbeef", Manifest(), confirm: false, Ct));

        Assert.Contains("confirm:true", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ApplyCreatesMissingSchemaAndAttachesProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnPostProperty(new JsonObject
        {
            ["id"] = "prop-etat",
            ["key"] = "etat_identification",
            ["name"] = "État d'identification",
            ["format"] = "select",
        });
        server.OnPostPropertyTag("prop-etat", new JsonObject
        {
            ["id"] = "tag-confirme",
            ["key"] = "confirme",
            ["name"] = "Confirmé",
            ["color"] = "grey",
        });
        server.OnPostType(new JsonObject
        {
            ["id"] = "type-piece",
            ["key"] = "piece",
            ["name"] = "Pièce",
            ["plural_name"] = "Pièces",
            ["layout"] = "basic",
        });

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", Manifest(), Ct);
        string previewId = PreviewId(preview);

        // A fresh gesture instance proves that apply depends only on the
        // manifest + deterministic preview contract, not process memory.
        string digest = await NewGestures(server).ApplyAsync(
            "home", previewId, Manifest(), confirm: true, Ct);

        Assert.Contains("propriété créée etat_identification", digest);
        Assert.Contains("tag créé etat_identification:Confirmé", digest);
        Assert.Contains("type créé piece", digest);
        Assert.DoesNotContain("propriétés attachées à piece", digest);

        JsonObject property = server.Requests
            .Where(r => r.Method == "POST" && r.Path.EndsWith("/properties", StringComparison.Ordinal))
            .Select(r => (JsonObject)JsonNode.Parse(r.Body)!)
            .Single();
        Assert.Equal("etat_identification", property["key"]!.GetValue<string>());
        Assert.Equal("select", property["format"]!.GetValue<string>());

        JsonObject typeCreate = server.Requests
            .Where(r => r.Method == "POST" && r.Path.EndsWith("/types", StringComparison.Ordinal))
            .Select(r => (JsonObject)JsonNode.Parse(r.Body)!)
            .Single();
        Assert.Equal("basic", typeCreate["layout"]!.GetValue<string>());
        Assert.Equal("Pièces", typeCreate["plural_name"]!.GetValue<string>());
        JsonArray links = Assert.IsType<JsonArray>(typeCreate["properties"]);
        JsonObject link = Assert.IsType<JsonObject>(Assert.Single(links));
        Assert.Equal("etat_identification", link["key"]!.GetValue<string>());
        Assert.Equal("État d'identification", link["name"]!.GetValue<string>());
        Assert.Equal("select", link["format"]!.GetValue<string>());
        Assert.DoesNotContain(server.Requests, r => r.Method == "PATCH");
    }

    [Fact]
    public async Task ApplyDoesNotRenameExistingTypeWhileAttachingProperties()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(name: "Nom live") }));
        server.OnListProperties(EmptyList());
        server.OnPostProperty(new JsonObject
        {
            ["id"] = "prop-etat",
            ["key"] = "etat_identification",
            ["name"] = "État d'identification",
            ["format"] = "select",
        });
        server.OnPostPropertyTag("prop-etat", new JsonObject
        {
            ["id"] = "tag-confirme",
            ["key"] = "confirme",
            ["name"] = "Confirmé",
            ["color"] = "grey",
        });
        server.OnPatchType("type-piece", ExistingType(name: "Nom live"));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", Manifest(), Ct);
        string previewId = PreviewId(preview);

        await gestures.ApplyAsync("home", previewId, Manifest(), confirm: true, Ct);

        JsonObject typePatch = server.LastBodyFor("PATCH");
        Assert.Equal("Nom live", typePatch["name"]!.GetValue<string>());
        Assert.Equal("Pièces", typePatch["plural_name"]!.GetValue<string>());
        JsonArray links = Assert.IsType<JsonArray>(typePatch["properties"]);
        JsonObject link = Assert.IsType<JsonObject>(Assert.Single(links));
        Assert.Equal("etat_identification", link["key"]!.GetValue<string>());
        Assert.Equal("État d'identification", link["name"]!.GetValue<string>());
        Assert.Equal("select", link["format"]!.GetValue<string>());
    }

    [Fact]
    public async Task ApplyCreatesTypeWithThePreviewedIcon()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnPostType(ExistingType(icon: new JsonObject
        {
            ["format"] = "icon",
            ["name"] = "home",
        }));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync(
            "home",
            TypeManifest(new JsonObject { ["format"] = "icon", ["name"] = "home" }),
            Ct);
        string previewId = PreviewId(preview);

        string digest = await gestures.ApplyAsync(
            "home", previewId,
            TypeManifest(new JsonObject { ["format"] = "icon", ["name"] = "home" }),
            confirm: true, Ct);

        Assert.Contains("set_icon · piece · icon:home", preview);
        Assert.Contains("icône définie piece · icon:home", digest);
        JsonObject create = server.Requests
            .Where(request => request.Method == "POST" && request.Path.EndsWith("/types", StringComparison.Ordinal))
            .Select(request => (JsonObject)JsonNode.Parse(request.Body)!)
            .Single();
        JsonObject icon = Assert.IsType<JsonObject>(create["icon"]);
        Assert.Equal("icon", icon["format"]!.GetValue<string>());
        Assert.Equal("home", icon["name"]!.GetValue<string>());
        Assert.Null(icon["color"]);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task ApplySetsIconOnlyWhenExistingTypeHasNone()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListProperties(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnPatchType("type-piece", ExistingType(icon: new JsonObject
        {
            ["format"] = "emoji",
            ["emoji"] = "🚪",
        }));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync(
            "home",
            TypeManifest(new JsonObject { ["format"] = "emoji", ["emoji"] = "🚪" }),
            Ct);
        string previewId = PreviewId(preview);

        string digest = await gestures.ApplyAsync(
            "home", previewId,
            TypeManifest(new JsonObject { ["format"] = "emoji", ["emoji"] = "🚪" }),
            confirm: true, Ct);

        Assert.Contains("set_icon · piece · emoji:🚪", preview);
        Assert.Contains("icône définie piece · emoji:🚪", digest);
        JsonObject patchBody = server.LastBodyFor("PATCH");
        JsonObject icon = Assert.IsType<JsonObject>(patchBody["icon"]);
        Assert.Equal("emoji", icon["format"]!.GetValue<string>());
        Assert.Equal("🚪", icon["emoji"]!.GetValue<string>());
        Assert.Single(server.Requests, request => request.Method == "PATCH");
    }

    [Fact]
    public async Task ApplyRefusesWhenTheLivePlanChangedAfterPreview()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListTypes(Page(new JsonArray
        {
            ExistingType(icon: new JsonObject
            {
                ["format"] = "icon",
                ["name"] = "home",
                ["color"] = "grey",
            }),
        }));
        server.OnListProperties(EmptyList());
        server.OnListProperties(EmptyList());

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync(
            "home",
            TypeManifest(new JsonObject { ["format"] = "icon", ["name"] = "bed" }),
            Ct);
        string previewId = PreviewId(preview);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestures.ApplyAsync(
                "home", previewId,
                TypeManifest(new JsonObject { ["format"] = "icon", ["name"] = "bed" }),
                confirm: true, Ct));

        Assert.Contains("set_icon · piece · icon:bed", preview);
        Assert.Contains("Relance schema_preview", error.Message);
        Assert.DoesNotContain(server.Requests, request => request.Method == "PATCH");
    }

    // ── Type descriptions ─────────────────────────────────────────────────────

    // The GET /objects/{id} face of the type, as the snapshot reader consults
    // it: a description only ever appears in the object's properties array —
    // the types surface has no such field in either direction.
    static JsonObject TypeObjectFace(string? description)
    {
        var properties = new JsonArray();
        if (description is not null)
            properties.Add(new JsonObject
            {
                ["key"] = "description",
                ["format"] = "text",
                ["name"] = "Description",
                ["text"] = description,
            });
        return new JsonObject
        {
            ["object"] = new JsonObject
            {
                ["id"] = "type-piece",
                ["properties"] = properties,
            },
        };
    }

    static JsonObject DescriptionManifest(string description) => new()
    {
        ["types"] = new JsonArray
        {
            new JsonObject
            {
                ["key"] = "piece",
                ["name"] = "Pièce",
                ["plural_name"] = "Pièces",
                ["description"] = description,
            },
        },
    };

    [Fact]
    public async Task PreviewPlansTheDescriptionWhenTheLiveTypeHasNone()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListProperties(EmptyList());
        server.OnGetObject("type-piece", TypeObjectFace(null));

        string digest = await NewGestures(server).PreviewAsync(
            "home", DescriptionManifest("Espace physique de la maison"), Ct);

        Assert.Contains("set_description · piece · Espace physique de la maison", digest);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    [Fact]
    public async Task PreviewSkipsADifferingLiveDescriptionAsConflict()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListProperties(EmptyList());
        server.OnGetObject("type-piece", TypeObjectFace("Autre texte, posé en app"));

        string digest = await NewGestures(server).PreviewAsync(
            "home", DescriptionManifest("Espace physique de la maison"), Ct);

        Assert.Contains("Conflits ignorés", digest);
        Assert.Contains(
            "set_description · piece · description existante « Autre texte, posé en app », "
            + "demandée « Espace physique de la maison »",
            digest);
        Assert.Contains("Aucune création additive nécessaire", digest);
    }

    [Fact]
    public async Task PreviewPlansNothingWhenTheLiveDescriptionMatches()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListProperties(EmptyList());
        server.OnGetObject("type-piece", TypeObjectFace("Espace physique de la maison"));

        string digest = await NewGestures(server).PreviewAsync(
            "home", DescriptionManifest("Espace physique de la maison"), Ct);

        Assert.Contains("Aucune création additive nécessaire", digest);
        Assert.DoesNotContain("set_description", digest);
    }

    [Fact]
    public async Task ApplyWritesTheDescriptionThroughTheObjectSurface()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListTypes(Page(new JsonArray { ExistingType() }));
        server.OnListProperties(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnGetObject("type-piece", TypeObjectFace(null));
        server.OnGetObject("type-piece", TypeObjectFace(null));
        server.OnPatchObject("type-piece", TypeObjectFace("Espace physique de la maison"));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync(
            "home", DescriptionManifest("Espace physique de la maison"), Ct);
        string previewId = PreviewId(preview);

        string digest = await gestures.ApplyAsync(
            "home", previewId, DescriptionManifest("Espace physique de la maison"),
            confirm: true, Ct);

        Assert.Contains("description définie piece", digest);
        JsonObject patch = server.LastBodyFor("PATCH");
        JsonArray properties = Assert.IsType<JsonArray>(patch["properties"]);
        JsonObject property = Assert.IsType<JsonObject>(Assert.Single(properties));
        Assert.Equal("description", property["key"]!.GetValue<string>());
        Assert.Equal("Espace physique de la maison", property["text"]!.GetValue<string>());
        Assert.Single(server.Requests, request => request.Method == "PATCH");
        Assert.EndsWith("/objects/type-piece", server.Requests.Single(r => r.Method == "PATCH").Path);
    }

    [Fact]
    public async Task ApplyWritesTheDescriptionOfAFreshlyCreatedType()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnPostType(ExistingType());
        server.OnPatchObject("type-piece", TypeObjectFace("Espace physique de la maison"));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync(
            "home", DescriptionManifest("Espace physique de la maison"), Ct);

        string digest = await gestures.ApplyAsync(
            "home", PreviewId(preview), DescriptionManifest("Espace physique de la maison"),
            confirm: true, Ct);

        Assert.Contains("create_type · piece", preview);
        Assert.Contains("set_description · piece · Espace physique de la maison", preview);
        Assert.Contains("type créé piece", digest);
        Assert.Contains("description définie piece", digest);
        JsonObject patch = server.LastBodyFor("PATCH");
        JsonObject property = Assert.IsType<JsonObject>(
            Assert.Single(Assert.IsType<JsonArray>(patch["properties"])));
        Assert.Equal("description", property["key"]!.GetValue<string>());
        Assert.Equal("Espace physique de la maison", property["text"]!.GetValue<string>());
    }

    // ── Sections ──────────────────────────────────────────────────────────────

    const string SectionCollectionId = "col-structure";

    // A section is one collection object (built-in type key "collection") whose
    // members are TYPE objects; the fixture declares the member type alongside.
    static JsonObject SectionManifest(bool declareType = true)
    {
        var manifest = new JsonObject
        {
            ["sections"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Structure",
                    ["icon"] = new JsonObject
                    {
                        ["format"] = "emoji",
                        ["emoji"] = "🧱",
                    },
                    ["types"] = new JsonArray { "floor" },
                },
            },
        };
        if (declareType)
            manifest["types"] = new JsonArray
            {
                new JsonObject { ["key"] = "floor", ["name"] = "Espace" },
            };
        return manifest;
    }

    static JsonObject ExistingFloorType() => new()
    {
        ["id"] = "type-floor",
        ["key"] = "floor",
        ["name"] = "Espace",
        ["plural_name"] = "Espaces",
        ["layout"] = "basic",
        ["properties"] = new JsonArray(),
    };

    // Search hit for the section collection. The search filter asks for the
    // built-in "collection" type; the hit carries type.key like the live API.
    static JsonObject CollectionHit(
        string name = "Structure",
        string id = SectionCollectionId) => new()
    {
        ["id"] = id,
        ["name"] = name,
        ["type"] = new JsonObject { ["key"] = "collection" },
    };

    // Preview reads issue one POST /search for sections; only that POST is a read.
    static bool IsWrite(FakeAnytypeServer.Received request) =>
        request.Method == "PATCH"
        || (request.Method == "POST" && !request.Path.EndsWith("/search", StringComparison.Ordinal));

    [Fact]
    public async Task PreviewReportsSectionCreationWithoutWriting()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnSearch(EmptyList());

        string digest = await NewGestures(server).PreviewAsync("home", SectionManifest(), Ct);

        Assert.Contains("Sections :", digest);
        Assert.Contains("Structure · création", digest);
        Assert.Contains("create_section · Structure", digest);
        Assert.Contains("add_to_section · Structure:floor", digest);
        Assert.DoesNotContain(server.Requests, IsWrite);
    }

    [Fact]
    public async Task PreviewReportsSectionReuseWhenACollectionBearsTheExactName()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingFloorType() }));
        server.OnListProperties(EmptyList());
        server.OnSearch(Page(new JsonArray { CollectionHit() }));

        string digest = await NewGestures(server).PreviewAsync(
            "home", SectionManifest(declareType: false), Ct);

        Assert.Contains("Structure · réutilisation de la collection existante", digest);
        Assert.DoesNotContain("create_section", digest);
        Assert.Contains("add_to_section · Structure:floor", digest);
        Assert.DoesNotContain(server.Requests, IsWrite);
    }

    [Fact]
    public async Task PreviewIdChangesWhenTheReusedSectionTargetChanges()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingFloorType() }));
        server.OnListProperties(EmptyList());
        server.OnSearch(Page(new JsonArray { CollectionHit(id: "collection-a") }));
        server.OnSearch(Page(new JsonArray { CollectionHit(id: "collection-b") }));

        string first = await NewGestures(server).PreviewAsync(
            "home", SectionManifest(declareType: false), Ct);
        string second = await NewGestures(server).PreviewAsync(
            "home", SectionManifest(declareType: false), Ct);

        Assert.NotEqual(PreviewId(first), PreviewId(second));
    }

    [Fact]
    public async Task PreviewRejectsSectionTypeUnknownInManifestAndSpace()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnSearch(EmptyList());

        string digest = await NewGestures(server).PreviewAsync(
            "home", SectionManifest(declareType: false), Ct);

        Assert.Contains("Conflits :", digest);
        Assert.Contains("section Structure : type demandé inconnu floor", digest);
        Assert.DoesNotContain(server.Requests, IsWrite);
    }

    [Fact]
    public async Task PreviewWithoutSectionsDoesNotSearchCollections()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());

        await NewGestures(server).PreviewAsync("home", Manifest(), Ct);

        Assert.DoesNotContain(
            server.Requests,
            request => request.Path.EndsWith("/search", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PreviewRejectsSectionWithoutTypeKeysBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["sections"] = new JsonArray
            {
                new JsonObject { ["name"] = "Structure", ["types"] = new JsonArray() },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("au moins une clé de type", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task PreviewRejectsNamedIconOnSectionBeforeReadingAnytype()
    {
        using var server = new FakeAnytypeServer();
        JsonObject manifest = new()
        {
            ["sections"] = new JsonArray
            {
                new JsonObject
                {
                    ["name"] = "Structure",
                    ["icon"] = new JsonObject
                    {
                        ["format"] = "icon",
                        ["name"] = "cube",
                        ["color"] = "grey",
                    },
                    ["types"] = new JsonArray { "floor" },
                },
            },
        };

        ArgumentException ex = await Assert.ThrowsAsync<ArgumentException>(
            () => NewGestures(server).PreviewAsync("home", manifest, Ct));

        Assert.Contains("icône nommée", ex.Message);
        Assert.Contains("emoji", ex.Message);
        Assert.Empty(server.Requests);
    }

    [Fact]
    public async Task ApplyCreatesSectionCollectionWithIconAndAddsMemberTypes()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(EmptyList());
        server.OnListProperties(EmptyList());
        server.OnSearch(EmptyList());
        server.OnPostType(ExistingFloorType());
        server.OnPostObject(new JsonObject
        {
            ["object"] = new JsonObject { ["id"] = SectionCollectionId, ["name"] = "Structure" },
        });
        server.OnPostListObjects(SectionCollectionId, "\"Objects added successfully\"");

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", SectionManifest(), Ct);
        string previewId = preview.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

        string digest = await gestures.ApplyAsync(
            "home", previewId, SectionManifest(), confirm: true, Ct);

        Assert.Contains("type créé floor", digest);
        Assert.Contains("section créée Structure", digest);
        Assert.Contains("icône définie Structure · emoji:🧱", digest);
        Assert.Contains("types ajoutés à Structure · floor", digest);

        JsonObject create = server.Requests
            .Where(r => r.Method == "POST" && r.Path.EndsWith("/objects", StringComparison.Ordinal)
                && !r.Path.Contains("/lists/", StringComparison.Ordinal))
            .Select(r => (JsonObject)JsonNode.Parse(r.Body)!)
            .Single();
        Assert.Equal("collection", create["type_key"]!.GetValue<string>());
        Assert.Equal("Structure", create["name"]!.GetValue<string>());
        JsonObject icon = Assert.IsType<JsonObject>(create["icon"]);
        Assert.Equal("emoji", icon["format"]!.GetValue<string>());
        Assert.Equal("🧱", icon["emoji"]!.GetValue<string>());

        FakeAnytypeServer.Received memberAdd = Assert.Single(
            server.Requests,
            r => r.Method == "POST"
                && r.Path.EndsWith($"/lists/{SectionCollectionId}/objects", StringComparison.Ordinal));
        JsonObject memberBody = Assert.IsType<JsonObject>(JsonNode.Parse(memberAdd.Body));
        JsonArray members = Assert.IsType<JsonArray>(memberBody["objects"]);
        Assert.Equal(["type-floor"], members.Select(node => node!.GetValue<string>()));
    }

    [Fact]
    public async Task SecondApplyReusesTheSectionAndOnlyReassertsMembers()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingFloorType() }));
        server.OnListProperties(EmptyList());
        server.OnSearch(Page(new JsonArray { CollectionHit() }));
        server.OnPostListObjects(SectionCollectionId, "\"Objects added successfully\"");

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", SectionManifest(), Ct);
        string previewId = preview.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];

        string digest = await gestures.ApplyAsync(
            "home", previewId, SectionManifest(), confirm: true, Ct);

        Assert.DoesNotContain("section créée", digest);
        Assert.Contains("types ajoutés à Structure · floor", digest);
        Assert.DoesNotContain(
            server.Requests,
            r => r.Method == "POST" && r.Path.EndsWith("/objects", StringComparison.Ordinal)
                && !r.Path.Contains("/lists/", StringComparison.Ordinal));
        Assert.DoesNotContain(
            server.Requests,
            r => r.Method == "POST" && r.Path.EndsWith("/types", StringComparison.Ordinal));
        Assert.Single(
            server.Requests,
            r => r.Method == "POST"
                && r.Path.EndsWith($"/lists/{SectionCollectionId}/objects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyRefusesWhenLiveStateNeedsActionsThatWereNotPreviewed()
    {
        using var server = new FakeAnytypeServer();
        server.OnListTypes(Page(new JsonArray { ExistingType(properties: new JsonArray { "prop-etat" }) }));
        server.OnListTypes(EmptyList());
        server.OnListProperties(Page(new JsonArray { ExistingProperty() }));
        server.OnListProperties(EmptyList());
        server.OnListPropertyTags("prop-etat", Page(new JsonArray { ExistingTag() }));

        var gestures = NewGestures(server);
        string preview = await gestures.PreviewAsync("home", Manifest(), Ct);
        string previewId = PreviewId(preview);

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => gestures.ApplyAsync("home", previewId, Manifest(), confirm: true, Ct));

        Assert.Contains("Relance schema_preview", ex.Message);
        Assert.DoesNotContain(server.Requests, r => r.Method is "POST" or "PATCH");
    }

    private static string PreviewId(string digest) =>
        digest.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1];
}
