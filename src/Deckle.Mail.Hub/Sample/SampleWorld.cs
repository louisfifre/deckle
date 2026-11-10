namespace Deckle.Mail.Hub;

// The synthetic mail of the sample, one scenario at a time. Every person,
// company and address is invented, on the domains the spec uses (exemple.fr,
// .example); the sentences are those of the frames, in French as the mails
// they stand for would be.
//
// The sorting's day is fixed on Monday 9 November 2026, the day the frames
// draw, so a proposed day reads « Fri 13 » and the menu « Mon 9 » on every
// run, whatever the clock says.
internal static class SampleWorld
{
    public static readonly DateOnly Today = new(2026, 11, 9);

    private static readonly TimeSpan Paris = TimeSpan.FromHours(1);

    private const string Fr = "fr";

    public static MailHubSnapshot Build(SampleScenario scenario)
    {
        if (scenario == SampleScenario.NoMailbox)
            return new MailHubSnapshot(Today, IsAnytypeConnected: true, [], [], [], []);

        bool cannotConnect = scenario == SampleScenario.MailboxCannotConnect;
        bool nothingWaiting = scenario == SampleScenario.NothingWaiting;

        List<Mailbox> mailboxes = Mailboxes(cannotConnect);
        List<MailSubject> subjects = [];
        List<ActivityEntry> activity = [];

        // The four subjects of a normal day; on a day already done they are
        // treated, and their gestures are today's lines of Activity.
        SubjectPlace today = nothingWaiting ? SubjectPlace.Treated : SubjectPlace.Waiting;
        subjects.Add(Drive(today));
        subjects.Add(FranceTravail(today));
        subjects.Add(Camille(today));
        subjects.Add(Devis(today));
        if (scenario == SampleScenario.Marks)
        {
            subjects.Add(Colis());
            subjects.Add(Releve());
        }
        if (nothingWaiting)
        {
            activity.Add(ByYou("a-drive", At(9, 9, 30), subjects[0], GestureResult.Task(Today)));
            activity.Add(ByYou("a-ft", At(9, 9, 35), subjects[1], GestureResult.Task(new DateOnly(2026, 11, 13))));
            activity.Add(ByYou("a-camille", At(9, 9, 41), subjects[2], GestureResult.Trash));
            activity.Add(ByYou("a-devis", At(9, 9, 44), subjects[3], GestureResult.Trash));
        }

        AddHistory(subjects, activity);
        AddSetAside(subjects, activity, awaitsEndOfDay: !nothingWaiting, franceTravailBack: !nothingWaiting);

        List<InfoLine> infos = nothingWaiting ? [] :
        [
            new("i-mutuelle", "mb-perso", null, "42,30 € remboursés, consultation du 28 octobre", Fr, "Mutuelle"),
            new("i-eau", "mb-maison", null, "Coupure d'eau jeudi 12 novembre, 9 h – 12 h", Fr, "Eau du Grand Lyon"),
            new("i-decheterie", "mb-perso", null, "Déchèterie fermée mercredi 11 novembre", Fr, "Mairie"),
        ];

        return new MailHubSnapshot(
            Today,
            IsAnytypeConnected: scenario != SampleScenario.AnytypeNotConnected,
            mailboxes, subjects, infos, activity);
    }

    private static List<Mailbox> Mailboxes(bool maisonCannotConnect)
    {
        var infomaniak = new MailServer("Infomaniak", "mail.infomaniak.com", 993);
        return
        [
            new Mailbox("mb-perso", "moi@exemple.fr", "Perso",
                [
                    new MailAlias("al-facture", "facture@exemple.fr"),
                    new MailAlias("al-shop", "shop@exemple.fr"),
                    new MailAlias("al-contact", "contact@exemple.fr"),
                ],
                MailboxConnection.Connected, HasSavedPassword: true, infomaniak,
                SortsByAlias: true, NamesAliasesFromAddress: true, At(9, 9, 41)),
            new Mailbox("mb-outils", "outils@exemple.fr", "Outils",
                [new MailAlias("al-alertes", "alertes@exemple.fr")],
                MailboxConnection.Connected, HasSavedPassword: true, infomaniak,
                SortsByAlias: true, NamesAliasesFromAddress: true, At(9, 9, 41)),
            // Added without a name: the window shows its address.
            new Mailbox("mb-maison", "maison@exemple.fr", null, [],
                maisonCannotConnect ? MailboxConnection.CannotConnect : MailboxConnection.Connected,
                HasSavedPassword: !maisonCannotConnect, infomaniak,
                SortsByAlias: false, NamesAliasesFromAddress: true,
                maisonCannotConnect ? At(9, 8, 12) : At(9, 9, 41)),
        ];
    }

    // ── The subjects of the day ──────────────────────────────────────────────

    private static MailSubject Drive(SubjectPlace place) => new(
        "s-drive", "mb-perso", "al-shop",
        "Commande drive Supermarché à retirer aujourd'hui, 17 h – 18 h", Fr, Today,
        [
            Mail("m-drive", "Supermarché Drive", "drive@supermarche.example", At(9, 7, 55),
                "Commande prête, retrait aujourd'hui 17 h – 18 h",
                "Bonjour,",
                "Votre commande n° 48213 est prête. Vous pouvez la retirer aujourd'hui entre 17 h et 18 h au point de retrait de votre magasin.",
                "Présentez le code de retrait 7731 à la borne, ou scannez le QR code de votre application.",
                "À tout à l'heure,\nVotre Supermarché Drive"),
        ], place);

    private static MailSubject FranceTravail(SubjectPlace place) => new(
        "s-france-travail", "mb-perso", null,
        "France Travail : actualisation à faire avant le 15 novembre", Fr, new DateOnly(2026, 11, 15),
        [
            Mail("m-ft", "France Travail", "ne-pas-repondre@francetravail.example", At(9, 8, 31),
                "Actualisation mensuelle à faire avant le 15",
                "Bonjour,",
                "Votre actualisation mensuelle est ouverte. Pour continuer à percevoir vos allocations, déclarez votre situation avant le 15 novembre depuis votre espace personnel.",
                "Cette démarche prend quelques minutes.",
                "Cordialement,\nVotre conseiller France Travail"),
        ], place);

    private static MailSubject Camille(SubjectPlace place) => new(
        "s-camille", "mb-perso", null,
        "Camille : choisir samedi ou dimanche pour la visite de la maison", Fr, null,
        [
            Mail("m-camille", "Camille", "camille@exemple.fr", At(2, 19, 20),
                "Samedi ou dimanche pour la visite ?",
                "Coucou,",
                "L'agence nous propose de visiter la maison ce week-end. Tu préfères samedi ou dimanche ? Dis-moi vite, ils attendent notre réponse pour caler le rendez-vous.",
                "Bises,\nCamille"),
        ], place);

    private static MailSubject Devis(SubjectPlace place) => new(
        "s-devis", "mb-maison", null,
        "Devis chauffe-eau de Plomberie Martin à valider", Fr, null,
        [
            Mail("m-devis-3", "Julien Martin", "devis@plomberie-martin.example", At(7, 10, 15),
                "Devis du chauffe-eau, 1 480 €, à valider",
                [
                    "Bonjour,",
                    "Suite à notre passage de jeudi, voici le devis pour le remplacement de votre chauffe-eau : un ballon électrique de 200 litres, la pose, l'évacuation de l'ancien et la mise en service, pour 1 480 € TTC.",
                    "Le devis est valable un mois. Si vous le validez, nous pouvons intervenir la semaine du 23 novembre ; il suffit de nous le renvoyer signé ou de répondre à ce mail.",
                    "Cordialement,\nJulien Martin\nPlomberie Martin",
                ],
                [new MailAttachment("Devis-2611.pdf", "PDF", 96 * 1024)]),
            Mail("m-devis-2", "You", "moi@exemple.fr", At(4, 18, 2),
                "Visite proposée jeudi matin",
                [
                    "Bonjour,",
                    "Merci pour votre retour. Jeudi matin me convient, je serai sur place à partir de 8 h 30.",
                    "Bonne journée",
                ],
                [], isFromYou: true),
            Mail("m-devis-1", "Plomberie Martin", "contact@plomberie-martin.example", At(3, 9, 5),
                "Demande de devis reçue, visite à fixer",
                [
                    "Bonjour,",
                    "Nous avons bien reçu votre demande de devis pour le remplacement de votre chauffe-eau. Nous devons passer voir l'installation : seriez-vous disponible jeudi matin ?",
                    "Cordialement,\nPlomberie Martin",
                ],
                []),
        ], place);

    // ── The marks of frame 2.5 ───────────────────────────────────────────────

    // A sender that failed the checks: the sentence names it by its address,
    // never by the name it gave itself, and the row offers the trash only.
    private static MailSubject Colis() => new(
        "s-colis", "mb-perso", null,
        "livraison@colis-suivi.example : frais de douane à régler pour un colis retenu", Fr, null,
        [
            Mail("m-colis", "Service Livraison", "livraison@colis-suivi.example", At(8, 23, 41),
                "Frais de douane de 2,99 € demandés sous 48 h",
                "Cher client,",
                "Votre colis est retenu au centre de tri. Des frais de douane de 2,99 € restent à régler sous 48 heures, faute de quoi il sera renvoyé à l'expéditeur.",
                "Réglez vos frais sur colis-suivi.example.",
                "Service Livraison"),
        ], SubjectPlace.Waiting, SubjectMark.SenderNotVerified);

    // A mail of images only: its own subject is its sentence, and it waits
    // with its reason rather than being set aside unread.
    private static MailSubject Releve() => new(
        "s-releve", "mb-perso", null,
        "Relevé de situation novembre", Fr, null,
        [
            Mail("m-releve", "Banque", "releves@banque.example", At(8, 6, 2),
                "Relevé de situation novembre",
                [],
                [new MailAttachment("Releve-novembre.pdf", "PDF", 212 * 1024)]),
        ], SubjectPlace.Waiting, SubjectMark.Unreadable);

    // ── What the person did before this day ──────────────────────────────────

    private static void AddHistory(List<MailSubject> subjects, List<ActivityEntry> activity)
    {
        MailSubject opticien = Treated("s-opticien", "mb-perso", null,
            "Opticien : lunettes à retirer avant le 14 novembre", new DateOnly(2026, 11, 14),
            "Optique du Centre", "contact@optique-centre.example", At(8, 9, 40),
            "Vos lunettes sont prêtes",
            "Bonjour,", "Vos nouvelles lunettes sont prêtes. Vous pouvez venir les retirer en magasin jusqu'au 14 novembre.", "À bientôt,\nOptique du Centre");
        MailSubject videGrenier = Treated("s-vide-grenier", "mb-perso", null,
            "Mairie : inscription au vide-grenier avant le 12 novembre", new DateOnly(2026, 11, 12),
            "Mairie", "vie-associative@mairie.example", At(8, 8, 15),
            "Inscriptions ouvertes au vide-grenier",
            "Bonjour,", "Les inscriptions au vide-grenier du 22 novembre sont ouvertes jusqu'au 12 novembre.", "Le service vie associative");
        MailSubject edf = Treated("s-edf", "mb-maison", null,
            "Relevé de compteur EDF à transmettre avant le 20 novembre", new DateOnly(2026, 11, 20),
            "EDF", "releve@edf.example", At(8, 7, 50),
            "Votre relevé de compteur est attendu",
            "Bonjour,", "Merci de nous transmettre le relevé de votre compteur avant le 20 novembre, depuis votre espace client.", "Le service client");
        MailSubject assurance = Treated("s-assurance", "mb-perso", "al-facture",
            "Assurance auto : échéance à régler avant le 1er janvier", new DateOnly(2027, 1, 1),
            "Assurance Horizon", "echeances@assurance-horizon.example", At(7, 10, 2),
            "Votre échéance annuelle",
            "Bonjour,", "Votre échéance annuelle d'assurance auto, de 412 €, est à régler avant le 1er janvier.", "Assurance Horizon");
        subjects.AddRange([opticien, videGrenier, edf, assurance]);

        activity.Add(ByYou("a-opticien", At(8, 18, 5), opticien, GestureResult.Task(new DateOnly(2026, 11, 12))));
        activity.Add(ByYou("a-vide-grenier", At(8, 18, 4), videGrenier, GestureResult.Trash));
        activity.Add(ByYou("a-edf", At(8, 8, 12), edf, GestureResult.Task(new DateOnly(2026, 11, 18))));
        activity.Add(ByYou("a-assurance", At(7, 11, 20), assurance, GestureResult.Task(new DateOnly(2026, 12, 30))));
    }

    // ── What the sorting set aside ───────────────────────────────────────────

    private static void AddSetAside(List<MailSubject> subjects, List<ActivityEntry> activity, bool awaitsEndOfDay, bool franceTravailBack)
    {
        void Add(string id, string mailboxId, string? aliasId, string sentence, SetAsideReason reason,
            DateTimeOffset at, string sender, string address, string body, bool today)
        {
            MailSubject subject = new(id, mailboxId, aliasId, sentence, Fr, null,
                [Mail($"m-{id}", sender, address, at, sentence, "Bonjour,", body, sender)], SubjectPlace.SetAside);
            subjects.Add(subject);
            activity.Add(new ActivityEntry($"a-{id}", ActivityPart.ByDeckle, at, id, mailboxId, aliasId,
                sentence, Fr, null, reason, AwaitsEndOfDay: today && awaitsEndOfDay));
        }

        Add("sa-jardinerie", "mb-perso", "al-shop", "Jardinerie : bulbes d'automne arrivés", SetAsideReason.Newsletter,
            At(9, 9, 12), "Jardinerie des Saisons", "news@jardinerie.example", "Les bulbes d'automne sont arrivés en magasin.", today: true);
        Add("sa-infomaniak", "mb-perso", "al-facture", "Facture Infomaniak de novembre disponible", SetAsideReason.Receipt,
            At(9, 8, 40), "Infomaniak", "facturation@infomaniak.example", "Votre facture de novembre est disponible dans votre espace client.", today: true);
        Add("sa-ft-offres", "mb-perso", null, "France Travail : offres de la semaine", SetAsideReason.Newsletter,
            At(9, 8, 30), "France Travail", "offres@francetravail.example", "Voici les offres de la semaine près de chez vous.", today: true);
        Add("sa-ft-atelier", "mb-perso", null, "France Travail : atelier CV en ligne", SetAsideReason.Newsletter,
            At(9, 8, 30), "France Travail", "ateliers@francetravail.example", "Un atelier CV en ligne est proposé jeudi.", today: true);
        Add("sa-doctolib", "mb-perso", null, "Doctolib : rappel du rendez-vous du 24 novembre, déjà confirmé", SetAsideReason.NoActionNeeded,
            At(9, 8, 2), "Doctolib", "rappels@doctolib.example", "Nous vous rappelons votre rendez-vous du 24 novembre, déjà confirmé.", today: true);
        Add("sa-rapport", "mb-outils", "al-alertes", "Rapport hebdomadaire du serveur, aucune alerte", SetAsideReason.NoActionNeeded,
            At(9, 7, 0), "Supervision", "supervision@outils.example", "Aucune alerte cette semaine.", today: true);
        Add("sa-fnac-points", "mb-perso", "al-shop", "Fnac : points fidélité bientôt expirés", SetAsideReason.Promotion,
            At(8, 18, 21), "Fnac", "fidelite@fnac.example", "Vos points fidélité expirent bientôt.", today: false);
        Add("sa-fnac-expedie", "mb-perso", "al-shop", "Fnac : commande expédiée", SetAsideReason.Receipt,
            At(8, 11, 5), "Fnac", "commandes@fnac.example", "Votre commande a été expédiée.", today: false);

        // A mail the person brought back from the sorting this morning: the
        // line stays, saying its subject is back in Triage.
        if (franceTravailBack)
            activity.Add(new ActivityEntry("a-ft-back", ActivityPart.ByDeckle, At(9, 8, 31), "s-france-travail", "mb-perso", null,
                "France Travail : actualisation à faire avant le 15 novembre", Fr, null, SetAsideReason.NoActionNeeded, Undone: true));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static MailSubject Treated(string id, string mailboxId, string? aliasId, string sentence, DateOnly? deadline,
        string sender, string address, DateTimeOffset at, string summary, params string[] body)
        => new(id, mailboxId, aliasId, sentence, Fr, deadline, [Mail($"m-{id}", sender, address, at, summary, body)], SubjectPlace.Treated);

    private static ActivityEntry ByYou(string id, DateTimeOffset at, MailSubject subject, GestureResult result)
        => new(id, ActivityPart.ByYou, at, subject.Id, subject.MailboxId, subject.AliasId, subject.Sentence, subject.Language, result, null);

    private static SubjectMail Mail(string id, string sender, string address, DateTimeOffset at, string summary, params string[] body)
        => new(id, sender, address, false, at, summary, body, []);

    private static SubjectMail Mail(string id, string sender, string address, DateTimeOffset at, string summary,
        IReadOnlyList<string> body, IReadOnlyList<MailAttachment> attachments, bool isFromYou = false)
        => new(id, sender, address, isFromYou, at, summary, body, attachments);

    // A moment of November 2026, Paris time.
    private static DateTimeOffset At(int day, int hour, int minute) => new(2026, 11, day, hour, minute, 0, Paris);
}
