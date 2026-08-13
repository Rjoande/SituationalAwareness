# Situational Awareness (SA) — Design Doc

Mod KSP 1.12.5: pannello di telemetria ambientale in volo — ora locale, fasi del
giorno, sole, atmosfera, posizione. Estetica "pannello digitale di avionica"
(ambra su scuro, monospace). Mod principalmente **estetica/immersiva**: in ogni
scelta, l'armonia con la UI stock e con il resto del modset vince sulla
precisione assoluta.

Stato: **design CHIUSO il 2026-07-17** (tutte le decisioni approvate dall'utente).
Mockup approvati: `notes/mockup-approvati.html`.

---

## 1. Requisiti funzionali

### 1.1 Campi mostrati

| Campo | Superficie | Orbita | Tidal lock (stella) | Note |
|---|---|---|---|---|
| Ora locale (grande) | ✔ | — (sostituita dal countdown) | — (sostituita da alert TIDAL LOCK) | **tempo medio su TUTTI i corpi** (`MeanTimeCalibration`, esteso da solo-home 2026-07-28, §9 punto 6), quantizzato per fuso — vedi §3 |
| Ora solare (SOLAR TIME, 3ª riga del dial) | opzione (default OFF) | — | — | tempo vero/apparente alla longitudine ESATTA (non quantizzato) — differenza concettuale con l'ora locale, §9 punto 6 |
| Fuso orario (TZ±n) | ✔ piccolo, accanto all'ora | — | — | |
| Sol (giorno locale) | ✔ | — | — | da UT 0, vedi §3.4 |
| UT | ✔ | ✔ | ✔ | via `dateTimeFormatter` (Kronometer-aware) |
| Ora KSC | ✔ | ✔ | ✔ | ora locale alle coordinate reali del KSC (`SpaceCenter.Instance`), NON `UT mod formatter.Day` — bug corretto in M1, vedi §3.6 |
| MET | opzione (default OFF) | opzione | opzione | ridondante con UI stock; la riga nascosta ricompatta la finestra (§6.6); formattata via `SaWindow.FormatDurationYDHMS`, §3.7 |
| Coordinate | ✔ | ✔ (punto sub-vessel) | ✔ | formato `12.4°N 87.9°E` |
| Bioma | ✔ | ✔ (sub-vessel) | ✔ | `ScienceUtil.GetExperimentBiomeLocalized(body, lat, lon)` — verificato M0, già localizzato |
| Elevazione sole | ✔ (`EL +34.2°`) | ✔ (riferita al punto sub-vessel) | ✔ | §4.2 |
| Azimut sole | ✔ (`AZ 247°`) | — | ✔ | §4.2 |
| Flusso solare (W/m²) | ✔ | ✔ | ✔ | §4.3 |
| Temperatura esterna (EXT TEMP) | ✔ solo se atmosfera | — | ✔ solo se atmosfera | **visibile in Superficie o Tidal Lock (mai in Orbita), E solo se il corpo ha atmosfera** (corretto 2026-07-27, due passaggi: prima da lettura di pressione live a mode+corpo, poi esteso da solo-Superficie a Superficie+Tidal Lock — confermato con un caso reale, un corpo GEP tidally locked sulla propria stella E con atmosfera, come nel mockup D). Deliberatamente non una lettura di pressione istantanea: se in futuro la modalità Superficie si estende ai voli suborbitali (§9 M3 punto 4), un veicolo temporaneamente sopra l'atmosfera ma ancora in modalità Superficie continua a mostrare "4K"/"VACUUM" invece di sparire e ricomparire; unità ciclabile °C/K condivisa con HULL TEMP; **campo `vessel.atmosphericTemperature`** (MAI `externalTemperature`); 3 fasce colore: ciano <0°C, neutro, giallo 50-100°C, rosso >100°C |
| Pressione (PRESSURE) | ✔ solo se atmosfera | — | ✔ solo se atmosfera | **segue EXT TEMP**, stessa condizione (Superficie o Tidal Lock, E atmosfera) — prima era sempre visibile con "VACUUM" nel vuoto |
| Temperatura scafo (HULL TEMP) | ✔ sempre | ✔ sempre | ✔ sempre | **nuova riga (M3 restyling)**: media pesata per `thermalMass` su tutte le parti (`SaReadoutProvider.BuildHullTemperature`, verificato `Part.thermalMass/temperature/maxTemp` pubblici sul decompilato) — la temperatura a cui la nave si stabilizzerebbe se perfettamente conduttiva, stessa quantità che l'integratore termico di KSP usa internamente; colore dal **rapporto peggiore** su tutte le parti, non dalla media — un pezzo vicino al limite deve colorare la riga anche se la media resta bassa (giallo >60%, rosso >80%). Per-parte il rapporto è `max(temperature/maxTemp, skinTemperature/skinMaxTemp)`, non solo il core: **bug corretto in retest 2026-07-27** — la prima versione guardava solo il core e restava bianca durante un rientro fatale, perché lo strato esterno (skin) si scalda per primo e più in fretta (il core si scalda più lentamente per conduzione interna); verificato sul decompilato che sia `Part.HeatGaugeUpdate()` (gauge overlay stock) sia il roll di esplosione per surriscaldamento in `FlightIntegrator` usano esattamente questo stesso max. Copre il caso "rientro/volo ipersonico" dove EXT TEMP non porta informazione utile ma la temperatura reale delle parti sì. Deliberata eccezione a §1.2 (esclusione dati nave): dichiarata esplicitamente come tale, non un dato ambientale |
| Pressione | ✔ | ✔ | ✔ | sempre visibile; **a 0 mostra "VUOTO"** (`#LOC_SA_vacuum`, EN "VACUUM") — più in stile Kerbal di "0.00 kPa" |
| Fase del giorno + dial | ✔ (6 fasi, 5 su corpi senza atmosfera — alba/tramonto fuse in "Terminator", §3.5) | ✔ (2 fasi, binarie: Sunlit/Eclipse — "Terminator" rimosso in M2, è un concetto di superficie) | ✔ (3 fasi statiche) | §3.5, §5 |
| Progressione giorno | ✔ (timeline sol, §6.3) | ✔ (barra luce/ombra orbita) | — (barra subsolare→antisolare) | |
| Tempo al prossimo evento | ✔ (alba/tramonto) | ✔ (eclissi/luce) — **eccezioni**: "STAR-CENTRIC" invece di "NEXT ECLIPSE/LIGHT" quando `r.BodyIsStar` (nessuna geometria di eclissi orbitando una stella); "SOI CHANGE" (timer punta al cambio SoI, Period forzato a infinito) su traiettoria di fuga (`patchEndTransition==ESCAPE`, priorità sull'eccezione stella) — entrambe corrette 2026-07-27, §3.7 | — (sostituito da distanza terminatore, §5.3) | Superficie e Orbita: entrambi via `SaWindow.FormatDurationYDHMS` (formato "timer" unificato, retest 2026-07-28/29 — prima la Superficie usava `FormatDuration`/stock, ritirata). Orbita aggiornata ogni frame renderizzato in `Update()` (non throttled a 10 Hz come il resto del pannello) via `SaReadoutProvider.TryBuildOrbitTimerFast`, §3.7/§6.10 |
| Distanza terminatore | — | — | ✔ (`412 km · 13.7° E`) | bidirezionale, §5.3 |
| Nome corpo / stella | ✔ header + footer (catena completa, §5.4) | ✔ | ✔ | `CelestialBody.displayName` (mai `.bodyName`/`.name`), **ripulito col tag di genere grammaticale Lingoona** (`LocalizeRemoveGender()`, API KSP ufficiale — non un fisso "^N": il tag dipende dal genere grammaticale della parola in quella lingua, es. confermato "Kerbin^N" ma "Sole^M" sul dizionario italiano installato; bug fix 2026-07-24, sostituisce sia il vecchio `Replace("^N","")` sia il toggle "Rename Sun to Kerbol", rimosso — il nome mostrato dipende ora solo dal planet pack/dalla sua localizzazione, nessun override SA) |
| Durata giorno solare | ✔ footer, "SOLAR DAY DdHhMm" (o "Xm Ys" sotto 1h) — campi zero omessi, home body sempre in ore, §6.4 | ✔ | footer mostra "SOLAR DAY infinite" | rinominato da "SOL" in M2 per non confondersi col Sol di calendario |
| Badge "LOCKED" | ✔ footer, qualunque corpo tidally locked (su stella o pianeta) | idem | idem (ridondante con l'alert, ma non sbagliato) | §5.4, rosso/alert |
| ASL / AGL | ✔ footer | footer mostra `ALT` | ✔ footer | km in orbita, metri altrove; separatore delle migliaia (M2) |
| Chip situazione | `SURFACE` | `ORBIT` | `TIDAL LOCK` (rosso alert) | header |
| Led CommNet | ✔ | ✔ | ✔ | verde/giallo/rosso/grigio, §6.5 |
| Gravità | ✔ | ✔ | ✔ | feature M2: percepita (default) o ASL fissa, unità g/m·s², §6.4 |
| Coordinate | ✔ | ✔ (sub-vessel) | ✔ | decimale (default) o DMS ciclabile, §6.4 |

**Nota — pulizia del nome, analisi completa (2026-07-24).** `^N` non è un
suffisso spazzatura fisso: è un **tag di genere grammaticale Lingoona**
(libreria reale usata da KSP, `LingoonaGrammarExtensions.cs` sul
decompilato), aggiunto dal traduttore stesso quando scrive la stringa per
la propria lingua, per permettere ad altre frasi con sostituzione
(`<<1>>`) di concordare articoli/preposizioni (es. italiano "atterrato
**sul** Sole" vs "**su** Duna"). La lettera del tag varia per parola e per
lingua — confermato dal dizionario italiano installato: `#autoLOC_910048
= Kerbin^N` ma `#autoLOC_910053 = Sole^M` (il Sole è maschile in
italiano, Kerbin no). Il vecchio `CleanDisplayName` (`Replace("^N","")`,
stesso approccio copiato da WDSP) funzionava per Kerbin ma falliva
silenziosamente su qualunque corpo taggato diversamente da N — su
un'installazione italiana il Sole sarebbe comparso come "SOLE^M" non
ripulito. **Fix**: `LocalizeRemoveGender()`, l'estensione ufficiale che
KSP stesso spedisce per questo scopo — taglia dall'ultimo `^` in poi,
qualunque lettera segua, no-op sicuro se non c'è alcun tag. Copre
entrambi i casi possibili per `Properties.displayName` nel cfg: stringa
esplicita già col tag (passa invariata attraverso `Localizer.Format`,
chiave non trovata) o riferimento a chiave di localizzazione (risolta al
valore della lingua attiva, tag compreso) — verificato leggendo
`Localizer._Format`/`ReplaceSingleTagIfFound` sul decompilato prima di
scrivere codice. **Rimosso di conseguenza il toggle "Rename Sun to
Kerbol"**: esisteva come toppa per un caso specifico (rinominare "Sun" in
"Kerbol"), ma confrontava il nome già localizzato contro il letterale
inglese `"Sun"` — non avrebbe mai funzionato su un'installazione non
inglese. Con la pulizia ora corretta, il nome mostrato dipende
interamente da cosa il planet pack/la sua localizzazione forniscono,
senza bisogno di override SA. **Resta invariato** (non tocca
displayName): il caso speciale colore-stella (`SaReadout.BodyIsSun`, §6.2)
confronta `body.bodyName` (identificatore interno, mai passato da
`Localizer`) contro il letterale `"Sun"` — quel confronto è già a prova
di lingua per costruzione, il problema qui era un altro campo.

### 1.2 Esclusioni deliberate

Nessun parametro orbitale (Ap/Pe/inclinazione/manovre), risorse/EC, dati
nave. SA è "ambiente e luce", non un computer di bordo: niente sovrapposizione
con KER/MechJeb. Densità atmosferica: esclusa dalla v1, annotata come
possibile riga opzionale futura.

**Nota**: la gravità locale era originariamente esclusa qui insieme ai
parametri orbitali; l'utente l'ha poi richiesta esplicitamente in M2 come
dato ambientale (non orbitale) — vedi riga Gravità in §1.1 e §6.4. La
distinzione che regge l'esclusione resta "niente meccanica orbitale", non
"niente fisica ambientale": la gravità percepita è coerente col resto del
pannello (temperatura, pressione, flusso), i parametri d'orbita no.

**Nota (M3 restyling, 2026-07-26)**: HULL TEMP (§1.1) è invece
un'eccezione dichiarata, non una reinterpretazione della regola — è
esplicitamente un dato **nave** (temperatura delle parti), non
ambientale. Motivata dal fatto che EXT TEMP smette di portare
informazione nel vuoto (legge una costante fisica) mentre in quello
stesso momento (rientro, volo ipersonico) la temperatura reale dello
scafo è l'informazione più rilevante del pannello — un pannello
avionico reale la mostrerebbe. Resta l'unica eccezione a questa regola;
non apre la porta a dati nave generici (niente Δv, TWR, risorse).

---

## 2. Modalità e selezione

Tre modalità, selezionate da **un'unica funzione** (`SaMode SaModeSelector.Select(Vessel)`)
isolata apposta perché il polishing di M3 interverrà lì.

**Ordine di valutazione (bug corretto dopo il test M1, fase 5): la
situazione del veicolo va controllata PRIMA del tidal lock, non dopo.** Il
tidal lock su stella cambia solo cosa significa "essere a terra" (nessuna
ora locale, sole fermo in cielo) — non dice nulla sulla meccanica orbitale.
Un veicolo in **orbita** attorno a un corpo del genere ha un ciclo
Sunlit/Terminator/Eclipse perfettamente normale (il blocco assiale del corpo
non influenza la sua orbita né la sua ombra): il primo test aveva il
controllo invertito e forzava sempre `TidalLock`, anche in orbita.

1. **SUPERFICIE o TIDAL LOCK** — solo se la situazione è
   `LANDED / SPLASHED / PRELAUNCH / FLYING`:
   - **TIDAL LOCK** se il corpo è bloccato marealmente **sulla propria
     stella**: `body.tidallyLocked && body.referenceBody è una stella`, con
     guardia su `solarDayLength` degenere. **Verificato M0**
     (`notes/verifiche-api.md` §1): in questo caso il gioco stesso restituisce
     esattamente `double.MaxValue` (non NaN/Infinity) — **valore finito**,
     quindi il guard corretto è una soglia esplicita
     (`solarDayLength > 1e17`), MAI `IsInfinity`/`!IsFinite` da soli. NB: il
     flag da solo NON basta — Mun è `tidallyLocked` verso Kerbin ma ha un
     normale ciclo solare (confermato in test, fase 4: la formula per una
     luna locked sul pianeta resta finita, nessun caso speciale lì).
   - **SUPERFICIE** altrimenti.
2. **ORBITA** — qualunque altra situazione (`SUB_ORBITAL / ORBITING /
   ESCAPING`), **indipendentemente** dal tidal lock del corpo.

**Edge case rimandati a M3 (predisposti, non implementati):** su corpi senza
atmosfera un salto EVA diventa subito `SUB_ORBITAL` ("in space low") → prevedere
in `SaModeSelector` una **soglia di quota AGL** sotto la quale il sub-orbitale
resta in modalità superficie; hop balistici brevi idem. La costante vive in un
punto solo, default iniziale da tarare in gioco.

Corpo = stella (`mainBody.isStar`): pannello ridotto (niente blocco solare),
gestito in M3. **Requisito concreto emerso dal retest M2 (2026-07-22)**:
in questo caso il footer deve nascondere del tutto la riga "SOLAR DAY ..."
(§5.4/§6.4) — una stella non ha un giorno solare rispetto a se stessa, il
dato non ha senso quando si orbita direttamente Kerbol/Grannus. Da coprire
insieme al resto di questo punto quando si affronterà M3.

---

## 3. Sistema orario

### 3.1 Ore di riferimento (decisione 1)

`N = round(formatter.Day / formatter.Hour)` da `KSPUtil.dateTimeFormatter` —
**mai** hardcoded. Stock → 6, JNSQ+Kronometer → 12, RSS → 24. Kronometer
sostituisce il formatter, quindi la compatibilità è automatica. Minuti e
secondi locali in base 60 (convenzione stock; basi custom di Kronometer =
limitazione nota documentata).

Ogni corpo ha ore locali di durata `L = solarDayLength / N` secondi UT
(es. Eve 22.5 h UT / 12 → ore locali da 1.875×, come il Mars Time JPL/MER).

### 3.2 Ora solare e longitudine subsolare

```
λ_sub = body.GetLongitude(starPosition)      // longitudine del punto subsolare,
                                             // già rotazione-aware (initialRotation + rotationAngle)
f(λ)  = ((λ − λ_sub + 180) mod 360) / 360    // frazione di giorno: 0 = mezzanotte, 0.5 = mezzogiorno
```

L'ora mostrata è `f · N` ore locali → `hh:mm:ss`. Il "Greenwich" di ogni corpo
è la longitudine 0 della sua mesh (decisione 2): nessun caso speciale, il KSC
cade nel suo fuso naturale.

**Rotazione retrograda (verificato M0, `notes/verifiche-api.md` §1-2):**
`GetLongitude` è puramente geometrico nel frame corotante del corpo — sia
`λ_sub` sia `λ` sono già coerenti col verso di rotazione, **nessuna
correzione manuale in `f(λ)` è necessaria**. L'unico punto dove il segno
conta è `solarDayLength`: per rotazione retrograda la formula del gioco
produce `solarDayLength < 0` (non `rotationPeriod` isolato). Usare
`abs(solarDayLength)` come durata; il segno stesso è il segnale definitivo se
mai servisse altrove. Nessun corpo JNSQ noto è retrogrado — edge case
documentato, non verificabile empiricamente allo stato attuale.

### 3.3 Fusi orari (decisione 2)

```
W  = 360 / N                                 // ampiezza fuso in gradi (12 ore → 30°)
k  = round(λ_normalizzata / W)               // indice fuso, con wrap; λ ∈ [−180, 180)
λc = k · W                                   // meridiano centrale del fuso
```

L'orologio mostrato è `f(λc) · N`: scorre fluido nel tempo, **non cambia
muovendosi dentro il fuso**, salta di 1 h al confine (il label `TZ±k` che
cambia spiega il salto). Fase, progresso giorno, Sol e prossimo evento sono
tutti calcolati **dal centro del fuso** `λc`, mai dalla longitudine esatta.

### 3.4 Sol (decisione 3)

Conteggio **da UT 0, per corpo**, allineato alla mezzanotte locale del fuso
corrente. **Bypassa l'offset epoca di Kronometer** (se il calendario stock
"Y1 D1" diventa "Y1969 D201", su Duna resta Sol 1): mai passare dal formatter
per il Sol, solo UT grezzo.

```
cycles = UT / solarDayLength                 // giorni solari reali trascorsi da UT0
f_now  = f(λc)                               // frazione di giorno corrente al centro fuso
f0     = frac(f_now − frac(cycles))          // fase del fuso a UT=0, in [0,1)
Sol    = 1 + floor(cycles + f0)              // incrementa alla mezzanotte locale
```

Nota di precisione: con orbite eccentriche l'ora solare apparente non avanza
in modo perfettamente uniforme (equazione del tempo) → il confine di Sol può
oscillare di poco. Accettato: mod estetica, orbite KSP quasi circolari.

**M2 — Sol non ha senso sul corpo home (Kerbin).** Mostrare un contatore Sol
lì è ridondante: coincide banalmente con l'Anno/Giorno già impliciti in UT.
Sostituire la riga SOL, sul solo home body, con **ANNO/GIORNO in stile
stock** (dal `dateTimeFormatter`, quindi Kronometer-aware) — **ma solo se è
installato Kronometer** (o un altro mod che sostituisce il formatter con uno
che applica un'epoca/offset narrativo, es. "Y1969 D201" invece di "Y1 D1"):
solo in quel caso Anno/Giorno porta un'informazione diversa da un contatore
Sol grezzo. Senza Kronometer, nascondere del tutto la riga sul corpo home.

**RITIRATA (2026-07-25).** In pratica, col calendario Kronometer
installato, la data Anno/Giorno mostrata qui (`PrintDateCompact(UT,
false)`) è la STESSA data già presente nella riga UT poco sotto (entrambe
derivano dallo stesso `dateTimeFormatter`) — puro doppione visivo,
segnalato dall'utente col proprio setup Kopernicus+Kronometer. La riga
sul corpo home ora è **sempre vuota** (altezza del blocco comunque
costante con gli altri corpi, che mostrano "Sol N"), indipendentemente da
Kronometer — semplifica anche il codice, non serve più distinguere
"Kronometer installato" da "non installato" per questa riga. Rimosso
`SaReadout.HasCustomCalendar` (era usato solo qui).

**M3+ (idea utente, non ancora in scope) — selettore Sol per gli altri
corpi.** Pagina Impostazioni stock con tre modalità per il conteggio Sol sui
corpi diversi dall'home:

1. **Universale** (comportamento attuale, decisione 3): tutti i corpi
   partono da Sol 1 a UT 0. Nessuno stato persistito, funziona da subito su
   qualunque corpo/salvataggio.
2. **Milestone**: Sol 0 per-corpo al primo allunaggio/atterraggio **in
   assoluto** su quel corpo (globale alla partita, non al singolo veicolo) —
   richiede un UT di riferimento per corpo persistito a livello di
   **ScenarioModule** (non di vessel: deve sopravvivere anche se il primo
   veicolo che è atterrato viene distrutto/recuperato).
3. **JPL-style**: Sol 0 per-**veicolo** al proprio primo atterraggio su quel
   corpo (stile missioni Mars rover reali) — richiede un UT di riferimento
   persistito nel **VesselModule** (stessa esigenza già annotata come
   estensione futura in una versione precedente di questa nota; qui diventa
   una delle tre opzioni del selettore, non l'unica alternativa a Sol
   universale). Esistono già mod MAS/RPM per il log missione, con cui questa
   modalità si sovrapporrebbe in parte — da tenere presente nel design
   quando si arriverà a implementarla.

Le tre modalità condividono la stessa formula di §3.4, cambia solo l'UT di
riferimento (0 vs UT del primo touchdown per-corpo vs UT del touchdown
per-veicolo) — nessun impatto sull'architettura di `SolarMath`, solo su cosa
`SaReadoutProvider` passa come "UT epoca" e su dove persistere quell'unico
numero extra per le modalità 2 e 3.

**Idea futura documentata (non in scope, segnalata dall'utente in M1)**:
overlay dei fusi orari sulla mappa di SCANsat, se presente — richiederebbe
uno sguardo all'API pubblica di SCANsat per capire se espone un hook di
overlay custom; da valutare quando il resto della mod è stabile.

**Idea futura documentata (non in scope, segnalata dall'utente nel retest
M2, 2026-07-21)**: per un'orbita retrograda, il marker veicolo sull'anello
del dial orbitale (§6.2) potrebbe muoversi in senso orario invece che
antiorario, per riflettere visivamente il verso reale del moto — da
valutare quando si arriverà a rifinire quella parte della UI.

**Idea futura documentata (2026-07-22, emersa dalla diagnosi §3.6)**: riga
opzionale "EQT ±mm:ss" — l'equazione del tempo (scarto istantaneo tra ora
solare vera e ora media) è un dato reale di navigazione astronomica,
perfettamente in tema col pannello avionico; su JNSQ oscilla di ±4.6 min
sull'anno, quindi è anche visivamente interessante. Da valutare dopo M3.

**IMPLEMENTATO 2026-07-28** (§9, M3 punto 6) come 3ª riga del dial
(SOLAR TIME, ciclabile a EqT), non "±mm:ss" fisso — vedi §3.7 per il
formato finale (`FormatLocalDurationYDHMS`, unità locali proporzionate
al giorno solare del corpo, non un semplice mm:ss).

### 3.5 Fasi del giorno (superficie)

Angolo orario dal centro fuso: `H = (f(λc) − 0.5) · 360` (0 = mezzogiorno,
±90 = terminatori, ±180 = mezzanotte). Senza tilt assiale (KSP non lo
supporta) alba/tramonto astronomici cadono SEMPRE a H = ∓90, a ogni
latitudine; la latitudine influenza solo l'elevazione.

Classificazione del fuso (tolleranza = mezza ampiezza fuso `W/2`):

| Fase | Condizione |
|---|---|
| MEZZOGIORNO | `abs(H) < W/2` |
| ALBA | `abs(H + 90) < W/2` (il fuso contiene il terminatore mattutino) |
| TRAMONTO | `abs(H − 90) < W/2` |
| MATTINA | `−90 + W/2 ≤ H < −W/2` |
| POMERIGGIO | `W/2 < H ≤ 90 − W/2` |
| NOTTE | il resto |

Prossimo evento (dal centro fuso, mostrato in unità UT via `dateTimeFormatter`
— decisione confermata): velocità apparente `rate = 360 / solarDayLength` °/s;
`t_tramonto = ((90 − H) mod 360) / rate`; `t_alba = ((270 − H) mod 360) / rate`;
si mostra il più vicino. Progressione giorno = `f(λc) · 100 %`.

**Etichetta su corpi senza atmosfera (M2, richiesta utente)**: ALBA e
TRAMONTO condividono la stessa etichetta **"Terminator"** quando
`!body.atmosphere` — senza aria non c'è crepuscolo ottico che li distingua,
solo l'attraversamento della linea giorno/notte. La classificazione interna
(e quindi la direzione mostrata nel countdown, alba vs tramonto) resta
invariata: cambia solo il testo mostrato per quelle due fasi.

### 3.6 Ora KSC e UT

- UT: `KSPUtil.dateTimeFormatter.PrintDateCompact(UT, includeTime, includeSeconds)`
  (**verificato M0** §4: firma esatta confermata, produce "Year N, Day M[,
  hh:mm:ss]" già localizzato) → formato e calendario di Kronometer gratis.
- **Ora KSC (ridefinita dopo il test M1, fase 1 — bug corretto):** NON è
  `UT mod formatter.Day` (quella formula ignora del tutto dove si trova
  davvero il KSC e in test mostrava lo stesso valore di UT). È l'**ora
  locale calcolata con la stessa matematica di §3.2/§3.3, ancorata alle
  coordinate reali del KSC** (`SpaceCenter.Instance.Latitude/Longitude`,
  corpo `SpaceCenter.Instance.cb`) invece che a quelle del veicolo. Queste
  coordinate sono calcolate dal gioco stesso dalla posizione reale del
  GameObject in scena (`notes/verifiche-api.md`, addendum M1) — corrette
  anche quando un planet pack riposiziona il KSC, senza bisogno di alcuna
  configurazione lato SA. Empiricamente il KSC (lon ≈ −74.5° stock) cade nel
  fuso −2/−3 a seconda di N, non nel fuso +0 — atteso, coerente con la
  decisione 2 (fuso +0 ancorato a lon 0 di ogni corpo, non al KSC).
- **Verificato M0**: senza Kronometer, `Day`/`Hour` del formatter di default
  sono costanti hardcoded (6h/24h via `GameSettings.KERBIN_TIME`), scollegate
  dalla fisica reale dell'home body — comportamento intenzionale per SA
  (decisione 1: armonia con l'orologio di gioco, bug compresi), non un
  problema da correggere.
- **Layout rivisto (2026-07-25)**: `(TZ±k)` spostato dal valore
  all'etichetta della riga ("KSC (TZ-3)" invece di "KSC TIME ... (TZ-3)"),
  su richiesta dell'utente — allinea visivamente i soli orari (HH:MM:SS)
  fra la riga UT e la riga KSC. Il fuso del KSC è stabile per l'intera
  sessione (le sue coordinate sono fissate al caricamento del sistema
  planetario, non cambiano senza un riavvio con un planet pack diverso —
  `notes/verifiche-api.md`, addendum M1), quindi il VALORE del fuso resta
  cachato una volta sola.
- **Correzione (M3 restyling, 2026-07-26)**: la visibilità del TZ era
  invertita rispetto a quanto l'utente intendeva davvero. Non "sempre
  visibile" — **nascosto di default, mostrato SOLO in modalità
  Superficie sul corpo home** (altrove non è pertinente: in orbita/tidal
  lock, o su un corpo diverso da quello del KSC, il fuso del KSC non ha
  alcun significato per ciò che il pannello sta mostrando). L'ETICHETTA
  (non il valore cachato) si ricalcola quindi ad ogni refresh, non solo
  alla ricostruzione della riga, perché dipende da modalità+corpo home
  che possono cambiare senza un rebuild della finestra (es. decollo con
  la finestra aperta).

### 3.7 Durate lunghe (timer, Period, MET) — `FormatDurationYDHMS`/`FormatLocalDurationYDHMS`

- **Aggiunto in retest 2026-07-27**, sostituisce il precedente
  `FormatOrbitPeriod` (HH:MM:SS/MM:SS fisso, ritirato). Richiesta
  utente: estendere a Y/D quando opportuno, **massimo 3 termini**.
- **Unificazione formati (retest 2026-07-28/29, go dell'utente)**: due
  famiglie globali di formattazione temporale in tutto il pannello —
  **"ora"** (orologio ciclico `HH:MM:SS`: LOCAL TIME, KSC, UT, SOLAR
  TIME formato Clock) e **"timer"** (contatori continui, sempre a
  lettere — mai due punti — `1y 278d 11h 39m 02s`). Regola del tetto:
  3 termini superiori per ogni timer TRANNE MET, che mostra sempre fino
  ai secondi (`MetMaxTerms = 5`). `FormatDurationYDHMS` prende ora un
  parametro `maxTerms` (default 3). "T−" resta un prefisso letterale
  davanti al timer dell'orbita, invariato — non fa parte della
  formattazione unificata.
- Confini di livello da `KSPUtil.dateTimeFormatter.Year/Day/Hour/Minute`
  (secondi-per-unità), **non** 31536000/86400/3600 fissi — stessa
  disciplina di §3.1/`BodyClock.LocalHoursPerDay`: segue il calendario
  attivo (stock, JNSQ+Kronometer, RSS...) invece di ricalcolare una
  fisica propria.
- **Simboli delle unità**: verificato sul decompilato (`KSPUtil.cs`,
  `DefaultDateTimeFormatter.PrintTime`) che stock espone chiavi di
  localizzazione dedicate per le lettere — `#autoLOC_6002317`=s,
  `6002318`=m, `6002319`=h, `6002320`=d, `6002321`=y — riusate
  direttamente via `Localizer.Format`, indipendenti da Kronometer
  (che cambia solo QUANTO dura un'unità, mai la PAROLA). Fallback alle
  nostre `#LOC_SA_unit_*` solo se la chiave stock non risolve
  (convenzione di `Localizer.Format`: ritorna la chiave stessa se non
  trovata).
- Usato per (variante **globale**, calendario `dateTimeFormatter`): riga
  MET (tetto 5, sempre ai secondi; prefisso letterale "T+" sempre —
  retest 2026-07-30), countdown "T−"/"Period"/SOI CHANGE in Orbita
  (estesa e strip), countdown alba/tramonto in Superficie (estesa e
  strip — **migrato** da `FormatDuration`/`PrintTimeCompact` stock,
  ritirata: prima era l'unica convenzione diversa dal resto, ora
  unificata), footer "SOLAR DAY" come "ora locale **relativa**"
  (**migrato** da `FormatSolarDayDuration`, bespoke, ritirata).
  **Bug corretto (retest 2026-07-30)**: sull'home body il caso
  speciale "solo ore" NON si ottiene gratis come inizialmente assunto
  — `fmt.Day` è calibrato per essere ESATTAMENTE il giorno solare
  dell'home body, quindi applicare il formatter globale al suo stesso
  `SolarDayLengthSec` dà sempre e comunque "1d 00h 00m" (circolare,
  inutile, non "12h 00m" come atteso). Ripristinata l'eccezione
  home-only, `FormatHomeSolarDayDuration` (salta i livelli anno/giorno,
  mostra solo ore+minuti) — stessa logica del vecchio
  `FormatSolarDayDuration` per questo caso, solo riscritta sull'
  infrastruttura condivisa.
- **MET formato alternativo "stockalike" (retest 2026-07-30)**: nuovo
  toggle Impostazioni `metStockalikeFormat` (`Interactible` condizionato
  a `showMissionTime` — grigio, non nascosto, se MET è spenta): 
  `T+1y 23d 03:14:09` — lettere per anno/giorno (omessi se zero), poi
  HH:MM:SS a due punti classico, invece del formato di default tutto a
  lettere.
- Usato per (variante **locale proporzionata**, nuovo
  `FormatLocalDurationYDHMS`, SOLO EqT — §9 punto 6): 1 giorno locale =
  `solarDayLengthSec` del corpo TARGET (non dell'home/eccSource usato
  per l'angolo), 1 ora locale = giorno locale / N (N =
  `BodyClock.LocalHoursPerDay`, lo STESSO globale già usato da LOCAL
  TIME — non un valore fisso separato: correzione esplicita
  dell'utente dopo un primo tentativo con soglia arbitraria, "la
  suddivisione in ore dipende sempre dallo standard adottato per
  Kerbin"). Nessun livello "anni locali": un'EqT che superi anche solo
  un'ora locale è già anomala (osservazione dell'utente — l'equazione
  del tempo è per natura una variazione dentro un fuso orario).
- Entrambe le varianti condividono `JoinDurationTerms` (helper comune):
  parte dal termine più grande NON-zero, non da un tetto fisso — un
  valore piccolo mostra meno termini invece di riempire con zeri
  iniziali (`"12m 34s"`, non `"00h 12m 34s"`). **Nessun padding a due
  cifre su alcun termine** (bug corretto, retest 2026-07-30: il padding
  c'era inizialmente sui termini non-guida, ma su un'orbita Kerbin
  molto alta il "T−" letterale ("T−65g 05o 58m") arrivava a
  sovrapporsi visivamente alla riga "Period" adiacente — la lettera
  disambigua già ogni termine, il padding aggiungeva solo larghezza
  per nulla; ora `"T−65g 5o 1m"`).
- **`shrinkWhenWide` (retest 2026-07-30)**: il solo taglio del padding
  non bastava del tutto — osservazione empirica dell'utente, regge fino
  a 3 termini quando il termine guida ha 1 cifra (`T-9h 58m 43s`), da 2
  cifre in su serve ridurre a 2 termini. Nuovo parametro booleano su
  `FormatDurationYDHMS`/`JoinDurationTerms`: toglie un termine dal
  tetto quando il valore guida è ≥10, indipendentemente da quale unità
  sia (la causa è la larghezza in caratteri, non un'unità specifica).
  Applicato SOLO ai 3 punti "T−" (clockMain/stripHot, font grande 26px,
  dove è stato osservato l'overlap con la riga vicina) — non a
  Period/MET/EqT/footer (font più piccolo, nessun overlap segnalato
  lì, e attivarlo ovunque avrebbe ridotto informazione senza un
  bisogno dimostrato). **Discusso e scartato**: saltare i termini a
  zero invece (es. `10h 0m` → `10h 43s`, salta direttamente ai secondi)
  — più informativo ma rischia di essere "caotico" (coppie di unità
  diverse nello stesso slot in momenti diversi, il lettore non può più
  assumere "qui c'è sempre ore+minuti"); il vantaggio informativo è
  comunque transitorio (dura al più un minuto prima che il termine si
  aggiorni naturalmente). Mantenuto lo zero esplicito, coerente con le
  convenzioni di cronometri/countdown del mondo reale, nessuno dei
  quali salta un'unità a zero.
- **Idea futura proposta, in attesa di conferma**: se questi due fix
  combinati non bastassero in altri casi estremi,
  `Text.resizeTextForBestFit` di Unity (ridimensionamento automatico
  del font per stare nei bordi del contenitore) è disponibile come
  rete di sicurezza — non ancora implementato.

**Idea futura documentata (2026-07-29, non in scope, in attesa di
verifica in gioco dei formati appena implementati)**: un'icona accanto
a ciascun orario/timer per distinguere a colpo d'occhio base **locale**
(proporzionata al giorno solare del corpo) da base **globale**
(calendario `dateTimeFormatter`/UT) — oggi la distinzione è implicita
nel contesto (EqT è locale, MET/Period sono globali) ma non marcata
visivamente. Da valutare dopo il retest.
- **Eccezione traiettoria di fuga (retest 2026-07-27)**: quando
  `vessel.orbit.patchEndTransition == ESCAPE` (verificato sul decompilato
  `Orbit.cs`/`PatchedConicSolver.cs`: ricalcolato ogni frame per la patch
  corrente del vessel attivo, indipendente da nodi di manovra o mappa
  aperta), il countdown "T−" punta a `orbit.EndUT - UT` (il cambio SoI,
  non la prossima eclissi/luce — geometria che qui non ha senso, il
  pianeta verrà abbandonato prima), l'etichetta adiacente diventa "SOI
  CHANGE" (`#LOC_SA_val_soiChange`, priorità sull'eccezione STAR-CENTRIC),
  e "Period" viene forzato a infinito — un periodo che non verrà mai
  completato non è un dato reale, anche se `vessel.orbit.period` stesso
  resta un numero finito quando l'eccentricità è ancora `< 1` (ellittica
  ma con apoapside oltre la SoI). La UI stock mostra comunque il periodo
  "matematico" in questo caso; SA no, deliberatamente.

**Indagine desync ora locale/KSC vs UT (retest M2, 2026-07-21).** Segnalato
di nuovo dopo il fix M2 (refresh spostato su `FixedUpdate`). Due ulteriori
ipotesi verificate e **smentite** sul decompilato, non per supposizione:

1. Nessun throttle rispuntato — confermato leggendo il codice attuale.
2. Disallineamento tra `dateTimeFormatter.Day` e `body.solarDayLength` —
   **smentito decompilando Kronometer stesso**: con `useHomeDay = true`
   (impostato dalla patch JNSQ, `JNSQ_Configs/Kronometer.cfg`), Kronometer
   assegna letteralmente `Clock.day.value = homeBody.solarDayLength` — lo
   stesso identico campo che leggiamo noi, byte per byte. Nessun
   disallineamento possibile per costruzione.

Calcolo manuale sui numeri di uno screenshot dell'utente (UT 01:32:51,
TZ−3): KSC atteso 04:32:51 (fuso opposto all'intuizione ma coerente con la
nostra convenzione di segno), mostrato 04:32:50 — **un solo secondo di
scarto**, compatibile con normale rumore di campionamento tra due orologi
calcolati indipendentemente (uno via `PrintDateCompact`, l'altro via
`GetLongitude`/trigonometria), non con un bug sistematico.

**Fix adottato ugualmente** (pragmatico, non "ultima spiaggia": con due
ipotesi cadute è la soluzione più robusta disponibile, non solo un
workaround): **calibrazione una tantum** (`Core/HomeClockCalibration.cs`).
Al primo utilizzo sul corpo home, si calcola l'offset tra UT e la fase del
fuso 0 con la geometria esistente (una volta sola); da quel momento in poi,
l'ora locale/KSC sul corpo home è **pura aritmetica su UT** (`(UT mod
solarDayLength + offset) mod solarDayLength`), mai più passando dalla
trigonometria. Elimina per costruzione qualunque rumore residuo, per il
solo corpo dove il confronto byte-a-byte con UT ha senso (1 ora locale = 1
ora UT solo lì — altrove la scala diversa rende il confronto irrilevante,
osservazione dell'utente). L'indice di fuso (longitudine → quale fuso)
resta calcolato via geometria (`GetLongitude`), che non ha problemi di
integrazione temporale essendo valutato di fresco ogni tick dalla posizione
attuale.

**CHIUSO — confermato in partita nuova (2026-07-22).** Il sintomo "peggiora
a ogni chiusura/riapertura" segnalato subito dopo il fix non era un bug del
calcolo: erano residui di installazioni precedenti della mod (DLL/PluginData
di versioni vecchie non rimossi). Con rimozione completa della cartella
`SituationalAwareness` da `GameData/`, riavvio del gioco e reinstallazione
pulita: partita nuova da UT=0, UT e ora locale/KSC combaciano fin da subito,
restano allineati con F5/F9 e timewarp alto, e su più salvataggi diversi
provati. Nessuna ricalibrazione ripetuta, nessuna deriva nel tempo — la
calibrazione una tantum si comporta esattamente come da progetto.

**Osservazione collaterale (non un bug SA, da tenere a mente): epoca UT
di JNSQ sfasata di 180° rispetto al mezzogiorno solare a lon 0.** A UT=0 di
una partita nuova, l'ora locale al KSC (fuso −3) mostra le 3:00 (alba) invece
di un valore coerente con "UT=0 = mezzanotte a longitudine 0". In altre
parole JNSQ/Kronometer sceglie l'istante UT=0 in modo indipendente dalla
posizione reale del sole a lon 0 — è una scelta di epoca del planet
pack/calendario, non calcolabile né correggibile lato SA in modo generale
(dipenderebbe dal planet pack installato, e SA non può assumere JNSQ).
**Non impatta la correttezza di SA**: `HomeClockCalibration` calibra
comunque contro la fase solare *reale* misurata al primo utilizzo (mai
contro l'assunzione "UT=0 = mezzanotte"), quindi l'ora locale resta corretta
indipendentemente da dove JNSQ abbia messo il proprio UT=0. Documentato qui
solo perché è il tipo di dettaglio che potrebbe confondere in un futuro
debug ("perché UT e ora solare non tornano alla longitudine 0?") — la
risposta è: scelta di epoca del planet pack, non un calcolo SA.

**Riaperto (2026-07-22): ~1s/giorno di deriva accumulata, dopo un riavvio
di KSP.** Con install pulita e partita nuova (chiusura sopra) il problema
era sparito; dopo un successivo riavvio di KSP l'utente ha rilevato una
deriva accumulata di circa 1 secondo al giorno fra ora locale/KSC e UT — una
NUOVA sessione, quindi una NUOVA calibrazione (i campi statici di
`HomeClockCalibration` si azzerano solo alla chiusura del processo KSP, mai
alla sola apertura/chiusura della finestra). La formula di `Zone0Seconds` è
priva di deriva per costruzione solo se `solarDayLength` è identico
bit-per-bit fra la chiamata di calibrazione e ogni chiamata successiva — ma
`BodyClock.SolarDayLengthAbsSeconds` rilegge `body.solarDayLength` dal vivo,
senza cache, ad ogni chiamata. Se quel valore live non fosse perfettamente
stabile tick a tick, l'offset (derivato dalla lettura al momento della
calibrazione) smetterebbe silenziosamente di combaciare col periodo usato
dopo — un errore di **tasso**, non solo di fase. Non provato in modo
diretto (richiede il log qui sotto), ma è l'unico varco strutturale
individuato nel codice attuale. **Fix applicati (2026-07-22)**:

1. **Congelamento della `solarDayLength`** insieme all'offset al momento
   della calibrazione — l'aritmetica successiva usa sempre e solo il valore
   congelato, mai più una rilettura live, ripristinando la garanzia
   "zero deriva per costruzione" a prescindere da eventuale instabilità del
   valore live.
2. **Ricalibro alla riapertura della finestra** (`HomeClockCalibration.
   Reset()`, chiamato da `SaWindow.Open()`) — rete di sicurezza: limita
   qualunque deriva residua a "tempo trascorso dall'ultima apertura", non
   più "tempo trascorso dall'avvio di KSP".
3. **Log temporaneo ad ogni tick** (`Debug.Log`, prefisso
   `[SA-CLOCK-DEBUG]`, in `KSP.log`) — registra ut, `solarDayLength` live,
   quella congelata e la differenza fra le due. Se il fix (1) è
   risolutivo, `delta` deve restare **sempre 0.000000E+000**: se la deriva
   ricompare comunque, `delta` dice subito se la causa è ancora quella
   (dovrebbe essere zero per costruzione, quindi un valore diverso da zero
   è già di per sé la prova) o se è altrove. Da **rimuovere** non appena
   l'indagine si chiude in modo definitivo.

**DIAGNOSI DEFINITIVA (2026-07-22): equazione del tempo. Non è un bug —
né di SA né del gioco.** Test dell'utente: >2h di sessione, ~1M righe di
log. Risultati: `delta` sempre esattamente zero (il congelamento funziona;
`solarDayLength` live è in realtà stabilissimo, l'ipotesi "valore live
rumoroso" è morta); NESSUNA deriva in-sessione (i due orologi sono in
lockstep perfetto, come da costruzione); ma l'**offset di calibrazione
varia da un avvio all'altro** (21579.13 → 21578.41 → 21578.40 → 21594.83 su
save diversi). Analisi dei numeri:

- il tasso di variazione dell'offset rispetto a UT è **identico anche
  attraverso save diversi** (1.1204e-4, 1.1203e-4, 1.1224e-4 s/s): è una
  funzione pura e liscia di UT, non un artefatto di campionamento/avvio;
- estrapolato a UT=0, l'offset vale **21600.000 esatti** (= mezzo giorno
  solare — lo stesso valore che l'utente ha usato nella patch Kronometer
  `offsetTime`, trovato empiricamente in modo indipendente);
- la config JNSQ (`JNSQ_Bodies/Kerbin.cfg`) mostra `eccentricity = 0.02`,
  `meanAnomalyAtEpoch = 0`, `epoch = 0`, `argumentOfPeriapsis = 0`: a UT=0
  Kerbin è al perielio E il sole è esattamente sopra lon 0 (mezzogiorno) —
  allineamento deliberato di JNSQ;
- previsione quantitativa dall'orbita (Sole JNSQ: geeASL 27.7, raggio
  175750 km → GM 8.39e18; anno = 1.5768e7 s = **365 giorni da 12h
  esatti**): tasso di deriva attuale previsto = 2e·cos(M)·(L/T) =
  **1.1201e-4** contro 1.1204e-4 misurato; **4.84 s/giorno** previsti
  contro 4.84 misurati. Combacia alla quarta cifra.

Fisica: `solarDayLength = 43200.000000` è il giorno solare **MEDIO**. Con
e=0.02 il moto apparente del sole non è uniforme (al perielio l'orbita
"ruba" più rotazione → giorno apparente più lungo, ora ~43204.84 s):
l'ora solare VERA oscilla attorno all'ora media con **ampiezza ±275 s
(±4.6 min) sull'anno JNSQ**, zero a UT=0, massimo ritardo ~giorno 91,
di nuovo zero a metà anno, anticipo nella seconda metà. SA calibra sul
sole VERO al momento della calibrazione → ogni sessione "fotografa"
l'equazione del tempo in quell'istante → costante diversa a ogni avvio
(−5 s a UT≈46k, −21 s a UT≈192k, esattamente ciò che l'utente vede come
"ritardo che varia tra ~5 e ~20 s"). Predizione falsificabile: stesso
save ricaricato allo stesso UT → stesso identico offset; l'offset dipende
solo dall'UT del save, non da quante volte si riavvia. La nota di §3.4
("equazione del tempo... Accettato") aveva previsto il fenomeno — ciò che
non era previsto è che Kronometer+patch fornisse un orologio LINEARE di
riferimento con cui il giocatore lo confronta al secondo.

**Decisione presa (2026-07-22, approvata dall'utente)**: KSC TIME/ora
locale sul corpo home è **tempo medio/civile** (funzione lineare fissa di
UT, lockstep permanente — come il mondo reale: GMT = Greenwich MEAN Time
esiste esattamente per questo problema), non tempo solare apparente. Dial,
fasi e alba/tramonto restano sul sole VERO (geometria live, invariata): la
meridiana segna il sole, l'orologio segna il tempo civile — esattamente
come nella realtà.

**Implementazione (`Core/HomeClockCalibration.cs`, 2026-07-22)**: niente
replica a mano della geometria a UT=0 (avrebbe richiesto reimplementare
`BodyFrame`/la compensazione di rotazione della stella di
`CelestialBody.CBUpdate` — rischio valutato troppo alto per il beneficio,
vedi `notes/verifiche-api.md` addendum). Soluzione più semplice: la
calibrazione resta live a qualunque UT come prima, ma si sottrae la
**correzione standard dell'equazione del tempo** (equazione del centro,
ordine e²: `ν − M ≈ 2e·sin(M) + 1.25e²·sin(2M)`, radianti, convertita in
secondi con `solarDayLength/(2π)`), usando solo `home.orbit.eccentricity`/
`home.orbit.getMeanAnomalyAtUT(ut)` (verificate sul decompilato, mai
toccata la catena di rotazione di `CelestialBody`/`Planetarium`). Essendo
per definizione lo scarto vero-medio, sottrarlo a QUALUNQUE istante di
calibrazione produce lo stesso identico orologio medio — non serve
ancorare esplicitamente a UT=0. Per un'orbita circolare (e=0) la
correzione è zero, nessun effetto su nulla che non abbia già questo
fenomeno. Validato numericamente sui dati reali PRIMA di scrivere il
codice (tasso previsto 1.1201e-4 vs 1.1204e-4 misurato, offset
estrapolato a UT=0 = 21600.000 esatti da due calibrazioni indipendenti) —
combacia esattamente con la patch Kronometer `offsetTime=21600`
dell'utente: stessa correzione, trovata indipendentemente da due strade
diverse (empirica in gioco, analitica sull'orbita).

Log `[SA-CLOCK-DEBUG]` esteso (temporaneo, da rimuovere a indagine
chiusa): ogni tick confronta l'orologio medio restituito con una lettura
live del sole vero, per rendere visibile in `KSP.log` la sinusoide
dell'equazione del tempo (ampiezza attesa ±~275s sull'anno JNSQ) invece di
doverla ricalcolare a mano.

**Verifica finale su un anno intero (2026-07-22, campionamento
dell'utente a T+0/+3/+6/+9/+12 mesi): PASS.** Offset di calibrazione
sempre 21600.00 ± 0.073 s in tutti e cinque i punti dell'anno, orologi in
lockstep visivo perfetto (locale = UT − 3h esatte in ogni campione).
Unica anomalia residua osservata: a +9 mesi, micro-ritardo visivo del
flip dei secondi ("ogni 3 secondi, 2 flip in ritardo di una frazione
<0.5 s, il terzo sincronizzato"). **Causa precisa identificata, due
ingranaggi:**

1. **Il residuo di calibrazione ±0.073 s è ESATTAMENTE il termine e³
   troncato dell'equazione del centro** (`−(e³/4)·sin M +
   (13/12)·e³·sin 3M`): verificato su tutti e cinque i campioni, previsto
   vs loggato coincidono alla 4ª cifra decimale (−0.0032/+0.0733/+0.0018/
   −0.0733/−0.0033 previsti; −0.0032/+0.0732/+0.0018/−0.0733/−0.0033
   loggati). Massimo a M=π/2 e 3π/2 (±3 e ±9 mesi), ~zero a inizio/metà/
   fine anno — per questo l'effetto "sparisce" a T+12.
2. **La resa visiva "2 su 3 con periodo 3 s" è l'interazione di quel
   residuo con la griglia di refresh reale**: l'accumulatore a 10 Hz
   scatta in pratica ogni 0.12 s, non 0.10 (visibile nel log: tick a
   .396/.516/.636/.756 — 6 FixedUpdate da 0.02 s: artefatto float,
   5×0.02f resta sotto 0.1f). Entrambe le righe (UT e locale) campionano
   lo stesso UT, ma i loro confini di secondo distano 0.073 s: se un
   campione cade in quella finestra, la riga locale flippa un frame di
   refresh (~0.12 s) dopo la riga UT; sennò flippano insieme. E siccome
   25 × 0.12 = 3.000 s esatti, l'allineamento campioni↔confini si ripete
   con **periodo esattamente di 3 secondi**, con la finestra da 0.073 s
   che cattura ~2 posizioni su 3 → "2 flip in ritardo, il terzo
   sincronizzato", alla lettera. Predizione verificabile: a +3 mesi lo
   stesso pattern esiste **specchiato** (residuo +0.073 → la riga locale
   flippa in ANTICIPO di un frame 2 volte su 3) — probabilmente non
   notato perché l'occhio cercava il ritardo.

**CHIUSO (2026-07-22, go dell'utente)**: aggiunti i termini e³ alla
formula dell'equazione del centro (`EquationOfTimeSeconds`) — residuo
massimo sull'intero anno JNSQ sceso da 0.073 s a ~1 ms, la finestra di
disallineamento diventa incampionabile dal refresh reale e l'effetto
visivo del "2 flip su 3" sparisce ovunque nell'anno. Rimosso anche il log
temporaneo `[SA-CLOCK-DEBUG]` (aveva esaurito il suo scopo: tre bug
inchiodati — deriva da rilettura live di `solarDayLength`, deriva
stagionale da equazione del tempo non corretta, segno invertito della
correzione — e la sinusoide dell'anno fotografata in diretta). Restano
permanenti: il congelamento di `solarDayLength` alla calibrazione e
`HomeClockCalibration.Reset()` agganciato a `SaWindow.Open()`.

**Correzione di segno (2026-07-22, stesso giorno — trovata dal log al
primo test)**: la prima build applicava la correzione col segno sbagliato
(sottratta invece che sommata), **raddoppiando** l'errore stagionale
invece di cancellarlo (offset 22150 all'estremo della sinusoide invece di
21600). Il log l'ha inchiodata subito, e ha anche validato tutto il resto
della formula: tre calibrazioni a punti radicalmente diversi dell'anno
(UT 46k / 7.9M / 11.8M, incluso l'estremo stagionale con
`eqTimeSeconds = −275.055` — esattamente l'ampiezza ±275.0 prevista dai
parametri orbitali) con il segno corretto convergono tutte a **21600.00 ±
0.07 s**; il residuo di 0.07 s all'estremo è esattamente il termine e³
dell'equazione del centro troncato dalla formula (max ~0.06 s su JNSQ) —
invisibile, il display tronca al secondo intero, quindi NON si aggiunge
il termine e³ (minimo cambiamento dopo un bug di segno). Convenzione
verificata empiricamente e documentata nel codice: `eqTimeSeconds` è
MEDIO−VERO nel frame di rotazione KSP (positivo appena dopo il perielio,
quando la meridiana ritarda sull'orologio medio) — quindi si SOMMA
all'offset basato sul sole vero.

**Fix disponibile, confermato in gioco dall'utente (2026-07-22): `offsetTime
= 21600` (secondi) nel nodo `DisplayDate` di Kronometer.** Kronometer
(`Kronometer/README.md`) espone per ciascun formatter (`PrintDate`/
`PrintDateNew`/`PrintDateCompact`) un parametro `offsetTime` (`double`) che
sposta il tempo **prima** del calcolo della data/ora — a differenza di
`offsetYear`/`offsetDay` (solo cosmetici, applicati dopo). Una patch
ModuleManager tipo:
```
@Kronometer:FOR[qualcosa_dopo_JNSQ]
{
	@DisplayDate
	{
		@PrintDate { @offsetTime = 21600 }
		@PrintDateNew { @offsetTime = 21600 }
		@PrintDateCompact { @offsetTime = 21600 }
	}
}
```
riallinea UT=0 alla mezzanotte reale a longitudine 0 su JNSQ. **Da fare al
momento del rilascio di SA**: includere questa patch come **cfg opzionale**
(non obbligatorio, non applicato di default da SA stesso — è specifico di
JNSQ, altri planet pack avrebbero un valore diverso o nessun bisogno) in una
cartella tipo `Extras/JNSQ-UT-fix/` col proprio `:NEEDS[JNSQ]`, con nota nel
README che spiega perché esiste e che non è parte del calcolo di SA. Non
implementato ora: è pura documentazione in vista del rilascio, nessun codice
SA coinvolto.

**Nota per il README (retest 2026-07-22, non un bug SA)**: scarto
occasionale e non deterministico di centesimi di secondo fra l'ORA LOCALE
mostrata da SA e l'**orologio UT stock** (non la riga UT propria di SA,
che resta sempre coerente con l'ora locale essendo calcolata dallo stesso
UT nello stesso frame). Probabile artefatto di rendering/campionamento di
KSP stesso (due elementi Text UGUI distinti — uno stock, uno di SA —
aggiornati in frame leggermente diversi), non qualcosa che SA possa
correggere lato proprio. Da segnalare nel README al momento della
pubblicazione come limite noto, non da rincorrere in codice.

**Aspetto da valutare (sollevato dall'utente 2026-07-22): estendere il
tempo medio a TUTTI i corpi, non solo l'home body?** Oggi
`HomeClockCalibration` (tempo medio, ancora deterministica) si applica
SOLO al corpo home; su tutti gli altri corpi `BuildSurface` usa ancora il
percorso geometrico originale (`SolarMath.DayFraction`, sole vero, live
ogni tick) — motivato all'epoca perché "1 ora locale = 1 ora UT" ha senso
di confronto solo sull'home body (altrove la scala diversa rende il
confronto irrilevante). Visivamente l'effetto di NON correggere sarebbe
trascurabile ovunque tranne l'home body (decimi di secondo su scale
temporali già enormemente diverse da UT). **Ma l'utente nota una buona
ragione di coerenza**: se in futuro si aggiunge una riga opzionale "ora
solare vera/meridiana" (idea già registrata prima, indipendente dal fuso
orario) accanto a "ora locale", il confronto concettualmente pulito è
"ORA LOCALE = tempo medio/civile" vs "MERIDIANA = tempo solare vero" — su
QUALSIASI corpo, non solo Kerbin, esattamente come nel mondo reale (GMT
vs meridiana locale). Senza estendere il tempo medio ovunque, "ora
locale" resterebbe true-time altrove e la futura riga meridiana
diventerebbe un doppione quasi esatto (a parte il fuso), invece di un
dato concettualmente distinto.

**Raccomandazione**: estendere. Richiede generalizzare
`HomeClockCalibration` da singolo stato statico (home-only) a una cache
per-corpo (es. `Dictionary<CelestialBody, (bool calibrated, double
offset, double frozenDay)>`), applicando la stessa correzione
dell'equazione del tempo (`EquationOfTimeSeconds`, già generica su
qualunque `CelestialBody.orbit`) ad ogni corpo alla sua prima
calibrazione, non solo a home. `Reset()` diventa "svuota la cache intera"
(chiamato comunque da `SaWindow.Open()`). Nessun rischio noto: per e=0
(orbite circolari) la correzione è già zero, quindi i corpi senza
eccentricità non cambiano comportamento. Non implementato ora (nessun
codice toccato su richiesta) — proposta per la prossima sessione di
codice insieme al fix dei puntini orbita e del toggle strip.

**IMPLEMENTATO 2026-07-28** (§9, M3 punto 6): esattamente come
raccomandato qui, con un'insidia in più scoperta solo generalizzando
sul serio — `EquationOfTimeSeconds` non può usare `body.orbit`
direttamente per un corpo qualunque (per una luna sarebbe l'orbita
sbagliata, attorno al pianeta non alla stella); vedi §9 punto 6 per il
fix (`StarOrbitingAncestor`) e per la nuova riga SOLAR TIME che ha
reso concreta la "buona ragione di coerenza" anticipata qui.
`HomeClockCalibration` → rinominata `MeanTimeCalibration`.

---

## 4. Astronomia

### 4.1 Stella di riferimento (multistar)

Porting del `KopernicusStarResolver` di RealBattery (config-walk su
`Kopernicus/Body` con `Template = Sun`, cache per corpo, fallback
`Planetarium.fetch.Sun`): nessuna dipendenza da Kopernicus, pienamente
compatibile con qualsiasi planet pack. Il nome della stella risolta va nel
footer. Nessun dato stock è mai assunto fisso: tutto letto dai corpi a runtime.

### 4.2 Elevazione e azimut

Elevazione: come `SolarElevationRad` di RealBattery (zenith da
`dot(up, sunDir)`, **snap a 0 sotto ±0.5°** contro il rumore float vicino
all'orizzonte).

Azimut: da `vessel.north` / `vessel.east` (vettori ENU già forniti da Vessel —
verifica firma in M0): `az = atan2(dot(sunDir, east), dot(sunDir, north))`,
normalizzato 0–360, 0 = Nord.

### 4.3 Flusso solare

**Verificato M0** (`notes/verifiche-api.md` §6): `FlightIntegrator` calcola
`vessel.solarFlux` sempre rispetto a `Planetarium.fetch.Sun`/`Bodies[0]`
hardcoded — **mai** risolto per-corpo lungo la catena `referenceBody`. Per un
corpo la cui stella vera è secondaria (es. un pianeta di Grannus in JNSQ),
`vessel.solarFlux` stock è calcolato rispetto a Kerbol, salvo eventuale patch
Harmony di Kopernicus non verificabile da questo decompilato (solo KSP base).
**Deciso in implementazione M1** (correzione rispetto al piano iniziale):
`vessel.solarFlux` usato **diretto**, non un calcolo proprio via StarResolver.
Motivo: il campo `luminosity` che `StarResolver` legge da
`Kopernicus/Body/ScaledVersion/Light/luminosity` non ha una semantica fisica
verificata — potrebbe essere un valore di calibrazione per il rendering della
luce Unity (luminosità percepita), non una luminosità assoluta in Watt
utilizzabile nella legge dell'inverso del quadrato. Costruire una formula
`L/(4π d²)` su un valore di cui non conosciamo l'unità reale sarebbe inventare
fisica, non implementarla. **M3**: test empirico su un corpo di Grannus (JNSQ,
già disponibile) per capire se Kopernicus corregge già `vessel.solarFlux` per
multistar, e solo allora eventualmente introdurre un calcolo proprio con
semantica chiarita.

---

## 5. Casi speciali

### 5.1 Orbita

Porting **verbatim** di `OrbitalIlluminationStatus` di RealBattery (versione
post-fix 2026-07-14: frame in-plane coerente da `r × v`, mai
`Orbit.GetOrbitNormal` mescolato a vettori world). Fornisce fase
Sunlit/Shadow + tempo alla prossima transizione.

- **Fasi mostrate (riviste dopo il test M2, 2026-07-19): SUNLIT / ECLISSI,
  binarie.** "Terminatore" è un concetto di **superficie** (attraversare una
  linea sul terreno) — in orbita non esiste un equivalente, solo
  illuminato/in ombra (motivazione dell'utente). Rimosso `Terminator` da
  `SaPhaseOrbit`; l'anello del dial continua comunque a disegnare la banda
  d'ombra geometrica reale (`TerminatorBandDeg`, invariata) — è una
  rappresentazione visiva della geometria fisica, non più legata a uno
  stato di fase testuale.
- Il posto dell'ora locale lo prende il **countdown** `T−mm:ss` alla prossima
  transizione, con etichetta ECLISSI/LUCE (l'informazione operativa vera:
  gestione pannelli). **Sotto-riga corretta (2026-07-25)**: mostrava anche
  la fase attuale ("Sunlit"/"Eclipse") su una seconda riga — ridondante e
  fuorviante (contraddiceva visivamente l'etichetta ECLISSI/LUCE sopra;
  la fase è comunque già nel dial). Riga lasciata vuota, altezza del
  blocco invariata.
- % luce orbita = `1 − θ/π` (da `orbitalShadowFrac`); barra luce/ombra al
  posto della timeline sol. **Palette rivista** (test M2: i due toni erano
  troppo simili): azzurro tenue per il lato illuminato, blu scuro per
  l'ombra, marker veicolo bianco.
- Coordinate e bioma = punto **sub-vessel** (`vessel.latitude/longitude`
  valgono anche in orbita). Elevazione sole riferita al punto sub-vessel,
  etichettata `(sub-v.)`.
- Footer: `ALT` (quota orbitale, con separatore delle migliaia) al posto di
  ASL/AGL.

**Bug corretto (2026-07-24): orbitando una stella direttamente (Sun,
Grannus...) mostrava "orbit lit 99%" e un puntino residuo dell'arco
notte.** Due cause distinte. (1) `OrbitIllumination.LitFraction` ha una
formula **indipendente** da `Status()` (`asin(raggio_corpo/semiMajorAxis)`)
che assume sempre "il corpo orbitato blocca la luce di una stella lontana
e separata" — assunzione priva di senso quando il corpo orbitato **è** la
stella (il suo raggio enorme dà un angolo non trascurabile invece di
zero). `Status()` gestiva già questo caso (il vettore `antiSun` degenera
a zero quando `body==star`, `phiRad` va esattamente a 0), ma
`LitFraction` non condivideva quella guardia. Fix: stessa guardia
`body==star → 1.0` aggiunta anche lì (nuovo parametro `star`). (2) Con
`phiRad` esattamente 0, l'arco d'ombra sul dial andava da −90° a −90°
(larghezza zero) — tutti i punti della polilinea collassano nello stesso
punto; i segmenti (lunghezza zero) vengono giustamente saltati, ma i
dischi di raccordo (aggiunti di recente) restano disegnati su ogni punto
sovrapposto, lasciando un disco pieno visibile. Fix:
`SaDial.UpdateOrbit` non passa più punti a `orbitShadow` quando
`phiDeg` è sotto una soglia minima (0.05°), invece di un arco a
larghezza zero.

**Bug corretto (retest 2026-07-30): "orbit lit 100%" fuorviante su
orbite molto alte.** Numericamente corretto (la frazione arrotondata
è davvero ≥99.5%), ma dal punto di vista di navigazione fuorviante —
un'eclisse reale esiste ancora e su un'orbita molto alta può durare
ore, non minuti, anche se sottende un angolo minuscolo. Nuovo
`SaWindow.FormatLitFraction`: mostra `#LOC_SA_val_orbitLitNearFull`
("`>99%`") quando il valore arrotonderebbe a 100 ma non è esattamente
1.0; il caso genuinamente esatto (`BodyIsStar`, dove
`OrbitIllumination.LitFraction` ritorna sempre letteralmente `1.0`,
nessuna geometria di eclisse possibile) resta "100%" pulito, nessuna
ambiguità lì. Applicato sia al dial esteso sia alla strip.

### 5.2 Tidal lock verso la stella (decisioni 4 e 6)

- Alert `TIDAL LOCK` (rosso/arancio) al posto dell'orologio; chip header rosso.
- Fasi: **GIORNO / TERMINATORE / NOTTE** dal solo angolo orario del punto
  (statico). Dial: sole fisso all'elevazione corrente, nessun arco di progresso.
- Niente progresso giorno né countdown → sostituiti dalla **distanza dal
  terminatore** (§5.3) e dalla barra statica subsolare→antisolare con cursore.

### 5.3 Distanza dal terminatore (solo tidal lock su stella)

**Bidirezionale**: si mostra sempre il terminatore **più vicino**, con punto
cardinale (E/W) verso cui si trova — a est del punto subsolare è quello
orientale, a ovest quello occidentale. Terminatori a `λ_sub ± 90`.

Semantica della distanza: **lungo il parallelo** — `d = R · cos(lat) · Δλ_rad`
— perché è la distanza che un rover percorre davvero guidando E/W (la
geodetica sarebbe più corta ma non corrisponde a nessuna manovra reale).
Formato: `412 km · 13.7° E`; unità ciclabile km/gradi (§6.4).

### 5.4 Badge `LOCKED` (decisione 4, generalizzato dopo il retest M1 fase 5)

Badge **`LOCKED`**, quando `body.tidallyLocked` è vero — **a prescindere da
cosa il corpo sia bloccato** (sulla stella o su un pianeta) **e a
prescindere dalla modalità del pannello** (superficie, orbita o tidal lock).
È informazione anagrafica del corpo, sta col resto dei dati del footer,
sempre visibile quando vera. **Colore rivisto dopo il test M2**: non più dim
ma evidenziato in rosso/alert (`#ff5c4d`, via rich text `<color>` sulla sola
sottostringa "LOCKED"), per farlo risaltare come nell'alert TIDAL LOCK.

**Formato footer riformulato (M2, richiesta utente)**: non più piatto
`BODY · STAR · SOL ...`, ma catena **`STELLA // PIANETA // LUNA (se
presente) · SOLAR DAY ... [· LOCKED]`** — si risale `body.referenceBody` dal
corpo attuale fino alla stella (inclusa), si inverte, si uniscono i
`displayName` con " // ". Esempio: orbitando Mun → `KERBOL // KERBIN // MUN
· SOLAR DAY 27d 34m` (formato §6.4: giorni/ore/minuti, campi a zero
omessi — vedi sotto); orbitando Kerbin direttamente → `KERBOL // KERBIN ·
SOLAR DAY 12h 06m` (il corpo home resta sempre in ore, mai giorni).

**Corretto (2026-07-22)**: se il corpo orbitato è esso stesso una stella
(`SaReadout.BodyIsStar`, da `body.isStar`, es. orbitando Kerbol o Grannus
direttamente), il segmento "SOLAR DAY ..." è ora omesso dalla catena —
non ha un giorno solare rispetto a se stessa. Resta comunque aperto il
resto del caso "corpo = stella" più ampio di §2 (pannello ridotto,
gestito in M3) — questo è solo il pezzo footer.

Caso originario (decisione 4): una luna tidally locked SUL PIANETA (es. Mun)
— ciclo solare normale, pannello superficie normale, badge nel footer.

**Bug corretto nel retest M1 (fase 5)**: badge generalizzato dopo che il test
ha mostrato un buco — orbitando attorno a un corpo bloccato SULLA STELLA
(es. Moho), il pannello non segnalava affatto il blocco (il campo dati
escludeva esplicitamente quel caso, pensato solo per il badge "lune sul
pianeta"). Ora il campo (`SaReadout.BodyTidallyLocked`) è il semplice
`body.tidallyLocked`, senza esclusioni: mostra il badge anche in orbita
attorno a un corpo star-locked, e anche nel pannello TIDAL LOCK stesso
(§5.2) — lì è ridondante con l'alert in testata, ma non sbagliato: sono due
viste dello stesso fatto (l'alert è la conseguenza operativa "niente
orologio", il badge è il dato anagrafico "questo corpo non ruota rispetto a
ciò che orbita").

### 5.5 Poli

Con tilt zero il sole attraversa comunque H = ±90 a ogni latitudine
(elevazione minuscola ma il ciclo esiste): niente giorno/notte polare da
gestire per gli *orari*. Al polo esatto azimut indefinito e longitudine
instabile → mostrare `—` sotto una soglia di `cos(lat)` (M3).

---

## 6. UI

### 6.1 Struttura

Shell UGUI in codice, porting del pattern KRAB/KRILL (canvas proprio, drag da
titlebar, lock input via InputLockManager on hover). **Lezione KRAB
obbligatoria**: ogni RectTransform creato a mano deve avere anchorMin,
anchorMax, pivot e sizeDelta TUTTI espliciti, mai lasciati al default.

Layout esteso (mockup A/C/D): header (titolo `SA // CORPO · NOME-NAVE`, chip
situazione, led CommNet) · corpo a 2 colonne (dial 138px | colonna dati) ·
footer (corpo · stella · giorno solare [· LOCKED] | quote). Colonna dati =
`VerticalLayoutGroup` (vincolo per il resize automatico, §6.6), righe
raggruppate in blocchi tempo / posizione / ambiente con divider.

**Bug corretto (retest 2026-07-30): larghezza colonna dati non fissa.**
`DataCol` non aveva un `preferredWidth` esplicito (`SaUi.Size(dataCol,
-1f, ...)`) — il layout deferiva quindi alla larghezza "preferita" dei
suoi figli, che per il `Text` di `clockMain` (il countdown "T−...")
scala con la LUNGHEZZA della stringa renderizzata. Una "T−..." lunga
poteva far ridistribuire/sovrapporre la riga condivisa con `clockSub`
("Period"), anche se la finestra stessa non cambia mai dimensione
orizzontale (solo `verticalFit` è impostato sul suo
`ContentSizeFitter`, non `horizontalFit`). Fix: `DataCol` ha ora una
larghezza fissa esplicita, `ExtendedWidth - DialColWidth -
VDividerWidth` (costanti nominate, non più numeri magici sparsi, per
evitare che le due larghezze vadano fuori sincrono se una cambia senza
l'altra).

### 6.2 Dial (sinistra)

Disegnato in vettoriale con `MaskableGraphic`/`OnPopulateMesh` (tecnica
`KrabCurveLine`), **mai glifi Unicode** (lezione KRAB del `⟳` non renderizzato):

**Spaziatura dial↔fase aumentata 4px→8px (retest 2026-07-30)** —
`VerticalLayoutGroup.spacing` di `dialCol` in `SaWindow.BuildBody`,
un'unica proprietà condivisa da tutte e 3 le modalità (Surface/Orbit/
TidalLock riusano lo stesso `dialCol`/`Handle.labelsArea`), quindi
nessuna deriva possibile fra modalità per costruzione — verificare
comunque in gioco che il maggior respiro resti coerente ovunque.

- Superficie: arco orizzonte + riempimento progresso + marker sole; sotto,
  fase + riga `giorno 64% · ↓ tramonto 2h39m`. **Bug corretto (test M2
  retest)**: dopo il tramonto l'arco restava "pieno" fino a mezzanotte
  (il progresso non veniva azzerato quando `!isDay`, solo bloccato al
  100%) — ora si svuota esattamente al tramonto, coerente con "arco =
  avanzamento della sola luce diurna".
- Orbita: anello orbitale + segmento ombra + marker veicolo; sotto, fase +
  `luce 71% · eclissi tra 12m40s`. Disco pianeta centrale: colore neutro di
  default (schiarito dopo il test M2, era troppo scuro), oppure — opzione
  in Impostazioni (default OFF) — il **colore mappa/orbita reale del
  corpo**. **Bug corretto (2026-07-22)**: leggeva
  `CelestialBody.orbitDriver.orbitColor`, un campo scollegato dal cfg che
  resta sempre al suo default `Color.grey` — decompilando anche
  `Kopernicus.dll` (`OrbitLoader.Color`) si è visto che il campo `color`
  del cfg scrive su `OrbitRenderer`/`OrbitRendererData` (componente
  diverso), mai su `OrbitDriver`. Fonte corretta (100% API stock, nessuna
  dipendenza da Kopernicus): `PSystemManager.OrbitRendererDataCache[body]
  .orbitColor` — cache statica pubblica popolata una tantum al
  caricamento del sistema planetario, sempre disponibile in volo. Il campo
  gemello `nodeColor` (colore pieno dell'icona) è `internal`, non
  accessibile da un plugin esterno; `orbitColor` è già a metà luminosità
  per scelta di Kopernicus stesso, quindi l'attenuazione extra di SA
  (`AttenuateMapColor`, ×0.6) è stata rimossa — il doppio scurimento
  rendeva il colore troppo spento/fangoso. **Confermato in gioco
  (2026-07-23).** **Caso speciale, solo Sole radice (2026-07-23,
  richiesta utente; corretto 2026-07-24)**: il vero Sole radice non ha un
  colore-orbita proprio (non orbita attorno a nulla, nessuna voce sensata
  in `PSystemManager.OrbitRendererDataCache` per lui) — orbitandolo
  direttamente il disco cadrebbe sul grigio di fallback. Hardcoded un
  giallo-oro fisso (`#ffcc33` circa) al posto del lookup in cache.
  **Correzione (2026-07-24)**: la condizione era inizialmente
  `r.BodyIsStar` (vero per QUALSIASI stella), ma una stella secondaria
  come Grannus orbita davvero il Sole radice e ha già un colore
  configurato sensato nella cache (es. rosso da nana rossa) — usare
  l'oro fisso anche lì l'avrebbe nascosto. Nuovo campo dedicato
  `SaReadout.BodyIsSun`, vero solo quando `body.bodyName == "Sun"` (il
  nome interno che ogni planet pack deve mantenere per la stella radice,
  come "Kerbin" per l'home body — indipendente dal `displayName`/dal
  toggle Kerbol). `BodyIsStar` resta invariato per la logica footer
  (§5.4, si applica correttamente a qualunque stella).
  **Artefatto puntini, storia dei
  fix**: (1) prima ipotesi (segmenti troppo radi) mitigata aumentando la
  suddivisione dell'arco 24→90 — riduce ma non elimina; (2) causa reale
  identificata rileggendo `SaVectorShape.cs`: ogni segmento di una
  polilinea è un quad estruso perpendicolarmente alla propria direzione,
  su una curva i quad di due segmenti consecutivi non condividono un
  bordo al vertice comune — si apre una fessura ad OGNI giunzione interna.
  Corretto con un disco arrotondato ad ogni vertice interno. **Tentativo
  di estenderlo anche ai due estremi aperti (2026-07-22): provato e
  RESPINTO dall'utente** — coprire anche gli estremi di `orbitShadow`
  eliminava i puntini ma introduceva dei "pallini" visibili ai bordi
  dell'ombra, esteticamente peggiori del difetto originale. Riportato allo
  stato precedente (`for i=1 to points.Count-2`, solo vertici interni).
  **Spostato in "problemi noti" (§11)**, non in caccia attiva.
- Tidal lock: linea orizzonte + sole statico all'elevazione reale; sotto,
  fase + `sole fisso EL +38° · nessun ciclo`.

### 6.3 Timeline (sotto le righe)

- Superficie: barra del sol — segmento ambra = finestra di luce (alba→tramonto),
  cursore = ora attuale, tick con orari locali di alba/tramonto.
- Orbita: barra luce/ombra del periodo orbitale con cursore.
- Tidal lock: barra statica subsolare→antisolare con cursore posizione.

### 6.4 Unità ciclabili (click sulla riga)

°C↔K (temperatura), kPa↔atm (pressione), km↔gradi (terminatore),
**decimale↔gradi/minuti/secondi (coordinate, aggiunto in M2)**,
**g↔m/s² (gravità, aggiunto in M2)**. Persistite globalmente in
`PluginData/settings.cfg` (MM non scansiona `PluginData/` — lezione KRILL).
Nel vuoto la pressione mostra `VUOTO` a prescindere dall'unità.
**Sotto 1 kPa (raffinamento da test M1, idea 2)**: passa automaticamente a
Pa invece di perdere precisione con due sole cifre decimali di kPa (atmosfere
molto sottili altrimenti mostrerebbero "0.01 kPa" con oltre il 25% di errore
relativo) — il ciclo manuale km/gradi-style resta comunque disponibile per
chi preferisce kPa/atm anche a quella scala.

**Formato DMS coordinate** (es. `91° 47' 02" S`): segno→emisfero (N/S per
lat, E/W per lon), gradi interi, minuti interi, secondi con un decimale.
**Layout corretto dopo il test M2**: il DMS è molto più largo del decimale
e, su una sola riga "lat / lon", sconfinava lateralmente oltre il bordo
della finestra invece di andare a capo — ora lat e lon vanno su **due righe
impilate** quando l'unità è DMS, e la riga cresce in altezza (16→28px)
invece che in larghezza: disturba meno il layout fisso della colonna dial.

**Riga gravità (feature M2, richiesta utente)**: default = valore **percepito
in tempo reale**, stessa formula e stesso limite del sensore stock
`sensorGravimeter` — verificato sul decompilato (`ModuleEnviroSensor`,
`SensorType.GRAV`): `FlightGlobals.getGeeForceAtPosition(pos).magnitude`, un
**vero m/s²** (non "g" nonostante il resto della UI stock usi g altrove —
`gMagnitudeAtCenter` è GM in SI), mostrato solo entro `altitude ≤ 3 ×
raggio_corpo` (oltre, "OUT OF RANGE" come fa il sensore stock). Impostazioni:
toggle per passare al valore **fisso ASL** (`body.GeeASL ×
PhysicsGlobals.GravitationalAcceleration`), indipendente dalla quota
attuale. Unità ciclabile g/m·s² sul click della riga.

**Tutti i valori numerici a decimali FISSI** (bug corretto dopo test M2: con
`"0.##"` un valore che oscilla rapidamente attorno a un multiplo di 0.1
cambia larghezza a ogni cifra guadagnata/persa — es. coordinate durante un
lancio — leggibile come "sfarfallio"). Formati fissi per campo: coordinate
`0.00`, elevazione/azimut/terminatore-gradi `0.0`, temperatura `0.0`,
pressione kPa `0.00`/atm `0.0000`, gravità `0.00`, flusso/percentuali
interi. Valori in metri/km del footer (ASL/AGL/ALT): separatore delle
migliaia (`N0`/`N1`, cultura invariante → virgola).

**Formato SOLAR DAY con giorni (2026-07-25, richiesta utente).** Sotto 1h:
invariato, `Xm SSs` (rotatori velocissimi). Da 1h in su: **il corpo home
resta sempre `Hh Mm`** (il suo giorno solare definisce "1 giorno locale":
mostrarlo come "1d" sarebbe circolare). Qualunque altro corpo passa a
`Dd Hh Mm`, dove un "giorno" è `BodyClock.LocalHoursPerDay` **ore locali**
(la stessa convenzione già usata ovunque nel pannello per fusi/ore, non un
fisso 24h) — verificato sull'esempio dell'utente: Mun a 324h con 12 ore
locali/giorno (JNSQ+Kronometer) = 27 giorni esatti. **Campi a zero omessi
ovunque cadano**, non solo agli estremi: `27d 00h 34m` → `27d 34m`;
`00d 11h 00m` → `11h`. Se tutti i campi risultassero zero (caso limite,
non dovrebbe capitare sopra la soglia 1h), fallback a `0m`.

### 6.5 Led CommNet

**Verificato M0** (`notes/verifiche-api.md` §7, `CommNet.NodeUtilities.
ConvertSignalStrength`): lo stock usa 5 livelli (None/Red/Orange/Yellow/Green)
con soglie esatte `>0.75 Green`, `>0.5 Yellow`, `>0.25 Orange`, `>1e-9 Red`,
altrimenti None. **Il nostro led a 4 stati** (raffinato da test M1, idea 4 —
la versione a 3 stati collassava "nessun segnale" e "segnale debolissimo"
nello stesso colore, nascondendo un cambio di stato reale) mappa:

```
verde  = stock Green   (signal > 0.75)
giallo = stock Yellow o Orange (0.25 < signal ≤ 0.75)
rosso  = stock Red             (0 < signal ≤ 0.25, connesso ma debolissimo)
grigio/spento = stock None     (nessun segnale/nessuna connessione)
```

Accessor: `vessel.Connection.Signal` (`CommNet.SignalStrength`),
`vessel.Connection.IsConnected`. Soglie definitive, non più placeholder.

### 6.6 Riga MET opzionale

Toggle nelle impostazioni stock (`GameParameters`, pattern `KrillParams`),
default OFF. `SetActive(false)` sulla riga + `VerticalLayoutGroup` →
la finestra si ricompatta da sola, nessuna riga vuota.

### 6.7 Strip compatta (mockup B1/B2/B3)

Doppio click sulla titlebar collassa la finestra a una riga (stessa finestra,
non una seconda): **mini-dial** · dato caldo (ora / countdown / alert) · fase
· dato di coda (prossimo evento / % luce / distanza terminatore) · **led
tondo** (bug corretto dopo test M2: era quadrato in entrambe le modalità, ora
`SaVectorDot` come i marker del dial — nessun asset, coerente col resto della
UI vettoriale) **in fondo alla riga** (bug corretto: era finito in testa).
Stato collassato persistito con la posizione.

**Mini-dial implementato (2026-07-25, M3 punto 3)** — fedele al mockup
B1/B2/B3, era rimasto un placeholder statico per tutto M2:
`SaDial.StripHandle`/`BuildStripIcon`/`UpdateStripIcon`, versione
semplificata di `Handle`/`Build`/`Update` alla scala dell'icona (~26×22),
stesse formule per-modalità (hourAngle/isDay/t per la superficie,
phi/theta per l'orbita, elevazione solare per il tidal-lock) così restano
sincronizzate per costruzione con la versione estesa, mai una
ri-derivazione separata. Elementi tagliati a questa scala perché
illeggibili: riempimento di progresso e orizzonte tratteggiato in
superficie, disco pianeta centrale in orbita — restano solo gli elementi
che il mockup stesso mostra (arco+sole, anello+ombra+marker,
orizzonte+sole fisso). **Colori orbita corretti (test M3)**: il primo
giro usava colori UI generici (`PanelEdge`/`Cyan`) invece della stessa
palette del dial esteso — ora `SaUi.OrbitLit` (anello), `SaUi.OrbitShadow`
(già corretto), `SaUi.OrbitMarker` (marker veicolo bianco).

**Bug corretto dopo test M2**: il passaggio esteso↔collassato ricentrava
l'intera finestra invece di mantenere fissa l'altezza della titlebar/strip
precedente — con pivot centrale, un cambio di altezza sposta entrambi i
bordi in modo simmetrico se non compensato. Fix: `LayoutRebuilder.
ForceRebuildLayoutImmediate` subito dopo la ricostruzione del contenuto (per
conoscere la nuova altezza in modo sincrono, non al frame successivo), poi
compensazione di `anchoredPosition.y` per tenere fisso il bordo superiore —
solo sul toggle, non alla primissima apertura della finestra.

**Bug residuo, tentativo respinto (2026-07-22)**: espandere (strip→esteso)
fa scendere leggermente la finestra invece di tenere fisso il bordo
superiore — collassare (esteso→strip) resta corretto. Ipotesi tentata:
doppia chiamata a `ForceRebuildLayoutImmediate` prima di leggere
`newHeight` (un `ContentSizeFitter` annidato può non convergere in una
sola chiamata quando il contenuto cresce molto, 1→~15 righe) — **provata
in gioco, non ha risolto il drift E ha introdotto propri artefatti
visivi**; riportata alla singola chiamata originale. **Spostato in
"problemi noti" (§11)**, non in caccia attiva — causa non ancora
identificata con certezza.

**Nota (idea utente M1, punto 1, chiusa in M2)**: persistenza di posizione/
stato collassato implementata in `PluginData/settings.cfg` (§7,
`SaSettings.cs`).

### 6.8 Skin e font (decisione 7)

Palette del mockup: ambra `#ffb000` (+dim `#8a5f00`), ciano `#4fd1c5`, alert
`#ff5c4d`, testo `#d8e2e4`/dim `#7d8e93`, pannello `#0f1517`/edge `#1e2a2e`.
Font: `Font.CreateDynamicFontFromOSFont("Consolas", …)` con **fallback al
font stock** se assente. Test M2 dedicato: forzare un nome font inesistente
per esercitare davvero il fallback. README (release): nota su possibile resa
diversa su Linux/macOS (Consolas assente → fallback stock).

**Colore della temperatura (richiesta utente post-M1)**: il valore della riga
TEMP EST è colorato per fascia — **blu/ciano sotto 0 °C** (freddo), colore
testo normale nella fascia intermedia, **rosso/alert sopra una soglia di
caldo** — così il colpo d'occhio distingue "troppo freddo" da "troppo caldo"
(il mockup colorava di rosso i −34 °C di Duna, ambiguo). Soglie proposte da
fissare in M2: < 0 °C ciano, > +50 °C alert, in mezzo neutro; costanti in un
punto solo, non sparse nella UI.

**Scala UI (retest 2026-07-27)**: slider `SaParams.uiScale` nei settings
(Difficulty → Situational Awareness), 0.5-2.0 in step da 0.05, default
1.0 (un default di 1.5 provato per primo risultò troppo aggressivo visto
in gioco). Applicato a `SaWindow`'s `CanvasScaler.scaleFactor`
(moltiplicato per `GameSettings.UI_SCALE`, non lo sostituisce) —
`ScaleMode.ConstantPixelSize` + `scaleFactor` scala in modo nitido a
qualunque valore (niente sfocatura da `Transform.localScale`), finestra
e font insieme, proporzioni automaticamente invariate. Ri-sincronizzato
ogni tick di refresh in `FixedUpdate` così un cambio nel pannello
impostazioni ha effetto senza dover riaprire la finestra (la finestra è
comunque nascosta mentre il pannello impostazioni è aperto, §6.10 —
niente vera anteprima live, solo niente bisogno di riaprire dopo).

### 6.9 Toolbar (decisione 8)

ToolbarControl (come KRILL), scena volo. Icona coerente con lo stile
(script `dev/make-icons.ps1`, pattern KRILL).

### 6.10 Refresh, visibilità di gioco (bug corretti dopo test M2)

**Ciclo di refresh**: legato a `FixedUpdate` con accumulatore a 10 Hz
(0.1s, precedente RealBattery), non più a `Update()`. Motivazione verificata
sul decompilato (`Planetarium.cs`): `Planetarium.time` (quindi `UT`) avanza
**solo dentro `FixedUpdate`** (`time += fixedDeltaTime * timeScale`), mai in
`Update()` — campionarlo da un `Update()` a piena frequenza di rendering
significava rileggere lo stesso valore congelato per più frame di fila e poi
"saltare" quando il prossimo tick fisico arriva. Campionare da `FixedUpdate`
elimina questa fonte di scarto per costruzione; l'accumulatore sopra limita
il ridisegno effettivo a ~10/s (già richiesto separatamente per il
"sfarfallio" dei decimali, §6.4). La convenzione di arrotondamento dei
secondi del formatter stock è stata verificata identica alla nostra (troncamento,
non arrotondamento) — non era quella la causa.

**Eccezione al throttle: timer orbitale (retest 2026-07-27/28)**.
L'intero pannello resta a 10 Hz — deliberato, `BuildHullTemperature`
percorre `vessel.parts` ad ogni refresh e su una nave grande quel costo
non va moltiplicato senza motivo. Ma il timer/Period di Orbita
(`OrbitIllumination.Status`, `vessel.orbit.period`) è puro calcolo O(1)
(trigonometria su pochi vettori, nessun ciclo) — tenerlo sullo stesso
throttle produceva un ritardo percepibile dopo una manovra lunga che
rimodella l'orbita. `SaReadoutProvider.TryBuildOrbitTimerFast`, chiamato
da `SaWindow.Update()` **una volta per frame renderizzato** (non
`FixedUpdate`: un primo tentativo lì restava comunque legato al tick
fisico, non al frame percepito dal giocatore — retest 2026-07-28),
quando `lastMode == Orbit`: aggiorna solo il timer/Period
(clockMain/clockSub estesa, o stripHot in strip), non il resto del
pannello. Condivide la formula con `BuildOrbit` via
`SaReadoutProvider.ComputeOrbitTimer` — i due percorsi non possono
disallinearsi. Lo stato fisico letto (posizione/velocità/`orbit.period`)
cambia comunque solo al ritmo dei tick fisici sottostanti — leggerlo da
`Update()` non perde precisione, aggiorna solo il testo visualizzato
prima.

**Visibilità stock**: la finestra ora si nasconde con **F2** (aggancio a
`GameEvents.onHideUI`/`onShowUI`, requisito standard per qualunque overlay
UI custom in KSP — omissione corretta, non era mai stato agganciato) e
**quando il menu di pausa (Esc) è aperto** (`PauseMenu.isOpen`, proprietà
statica pubblica verificata sul decompilato — prima la finestra restava
sopra il menu perché nulla la nascondeva). Entrambe le condizioni
controllano lo stesso `Canvas.enabled`, verificate ogni frame renderizzato
(leggero, non nel ciclo dati a 10 Hz) per reattività immediata.

---

## 7. Architettura codice

Separazione netta calcolo/UI: il provider produce una **struct `SaReadout`**
con tutti i valori già calcolati (numeri puri + enum, zero stringhe UI);
la finestra si limita a formattare. Refresh a ~5 Hz (clock aggiornato almeno
1×/s); niente allocazioni per frame (string cache / StringBuilder).

```
GameData/SituationalAwareness/
├── CLAUDE.md                       ← hub di progetto
├── SA_design_doc.md                ← questo file
├── Localization/en-us.cfg          ← master EN, #LOC_SA_*, commento per riga
├── Plugins/SituationalAwareness.dll
├── PluginData/                     ← settings runtime (posizione, unità, collapsed)
├── notes/                          ← appunti, checklist test, mockup approvati
└── src/                            ← net472, refs KSP_x64_Data/Managed
    ├── SituationalAwareness.csproj
    ├── Directory.Build.props       ← obj/bin su C: (%LOCALAPPDATA%\SituationalAwareness — regola exFAT)
    ├── Core/
    │   ├── StarResolver.cs         ← porting KopernicusStarResolver (RealBattery)
    │   ├── BodyClock.cs            ← cache per-corpo: solarDay, N, W, flag lock, retrogrado
    │   ├── SolarMath.cs            ← λ_sub, f, fusi, Sol, fasi, eventi, elevazione/azimut, terminatore
    │   ├── OrbitIllumination.cs    ← porting OrbitalIlluminationStatus (RealBattery, post-fix)
    │   ├── SaModeSelector.cs       ← selezione modalità (UNICO punto, per il polishing M3)
    │   ├── SaReadout.cs            ← struct dati puri + enum fasi/modalità
    │   ├── SaReadoutProvider.cs    ← orchestrazione: Vessel → SaReadout
    │   └── SaSettings.cs           ← GameParameters (MET) + persistenza PluginData
    └── UI/
        ├── SaUi.cs                 ← factory + palette + font (Consolas→fallback)
        ├── SaWindow.cs             ← finestra estesa + strip collassata
        ├── SaDial.cs               ← dial vettoriale (3 varianti) + timeline
        └── SaToolbar.cs            ← ToolbarControl, scena volo
```

Convenzioni: namespace `SituationalAwareness`; commenti codice in inglese;
zero stringhe hardcoded in UI (tutto `#LOC_SA_*`); file piccoli a
responsabilità chiara (lavorabile da Sonnet 5).

---

## 8. Localizzazione

Master `Localization/en-us.cfg`, ogni riga con commento-guida per i
traduttori. Gruppi di chiavi previsti (lista definitiva a M1/M2):

- Etichette righe: UT, KSC TIME, MET, COORDINATES, BIOME, SUN, FLUX,
  EXT TEMP, PRESSURE, TERMINATOR, LOCAL TIME.
- Fasi superficie: DAWN, MORNING, NOON, AFTERNOON, DUSK, NIGHT.
- Fasi orbita: SUNLIT, TERMINATOR, ECLIPSE. Fasi tidal lock: DAY, TERMINATOR, NIGHT.
- Stati: TIDAL LOCK, LOCKED (badge lune), VACUUM (`#LOC_SA_vacuum`), SURFACE,
  ORBIT, Sol, TZ.
- Toolbar/tooltip, titolo finestra, testo impostazioni.

Riciclare chiavi stock `#autoLOC_*` dove esistono equivalenti esatti
(pattern KRILL).

---

## 9. Milestone

### M0 — Verifiche sul decompilato (nessun codice mod) — **CHIUSA 2026-07-17**

Fonti: `<radice KSP>/Claude/ksp-decomp-key/`, `ksp-decomp-full.zip`
(estratto nella scratchpad — regola exFAT). Esito completo in
`notes/verifiche-api.md`. Nessun blocco architetturale: tutte le assunzioni
del design doc erano corrette o affinate con dati certi (correzioni già
riportate nelle sezioni pertinenti sopra). Checklist (tutte chiuse):

1. `CelestialBody.solarDayLength`: formula confermata; star-lock →
   `double.MaxValue` esatto (finito, guard a soglia non a `IsInfinity`);
   retrogrado → segno di `solarDayLength` stesso, non di `rotationPeriod`.
2. `CelestialBody.GetLongitude(Vector3d)`: geometrico nel frame corotante,
   nessuna correzione manuale per retrogradi necessaria in `f(λ)`.
3. `tidallyLocked`, `isStar`, `rotationPeriod`: campi diretti, criterio
   §2 confermato.
4. `KSPUtil.dateTimeFormatter`: firme confermate; senza Kronometer `Day`/`Hour`
   sono costanti hardcoded scollegate dalla fisica reale (intenzionale, §3.6).
5. `Vessel.north/east/up`: campi diretti, confermati per l'azimut.
6. `vessel.solarFlux`: hardcoded su `Planetarium.fetch.Sun`/Bodies[0], MAI
   risolto per-corpo — promosso il calcolo StarResolver a fonte primaria (§4.3).
7. CommNet: soglie stock esatte trovate (`NodeUtilities.ConvertSignalStrength`)
   — placeholder 25% sostituito da soglie definitive (§6.5).
8. Temperatura ambiente: **`atmosphericTemperature`**, mai `externalTemperature`
   (include shock heating da rientro) — confermato in §1.1/§4.
9. `ScienceUtil.GetExperimentBiomeLocalized(body, lat, lon)`: firma diretta,
   più semplice del previsto.

### M1 — Core matematico + finestra debug

Progetto C# compilante; `Core/*` completo; finestra **grezza** (righe testuali
con TUTTI i valori del readout, shell portata da KRAB, nessuna skin) + toolbar.
Checklist `notes/test-m1.md`: KSC fermo (coerenza ora KSC / ora locale / TZ);
aereo veloce (l'ora NON scorre con la longitudine, scatta al confine fuso col
TZ che cambia); orbita bassa (countdown vs ombra osservata); Mun (badge
LOCKED, ciclo solare normale); corpo JNSQ non stock; timewarp alto (stabilità
valori); Sol invariato rispetto all'offset epoca Kronometer.

### M2 — Skin definitiva + localizzazione — **CHIUSA 2026-07-24**

Palette ambra, dial vettoriali (3 varianti), timeline, strip collassabile
(B1/B2/B3), led CommNet a 4 stati, unità ciclabili (con soglia Pa sotto 1 kPa),
colore temperatura per fascia, riga MET opzionale con ricompatto, "VUOTO",
font Consolas + fallback (test dedicato documentato in `notes/test-m2.md`).
Localizzazione completa verificata (zero stringhe hardcoded — diff codice↔loc
senza scarti in nessuna direzione). Riga SOL sostituita da ANNO/GIORNO sul
solo home body, condizionata a un formatter di calendario non-stock
installato (§3.4). Persistenza (`PluginData/settings.cfg`): unità, posizione
finestra, stato collassato. Checklist `notes/test-m2.md` (molteplici giri di
retest, storia completa in `CLAUDE.md` voci 1-26). Diversi giri di
correzione visiva dopo il primo rendering reale in gioco, come previsto —
inclusa un'indagine approfondita sul desync ora locale/UT (risolta come
equazione del tempo, non un bug, §3.6) e la pulizia gender-aware dei nomi
corpo/stella (§1.1). **Due problemi cosmetici noti, non bloccanti,
registrati e non in caccia attiva**: dial orbita (puntini residui
nell'arco notturno di alcune configurazioni geometriche) e toggle strip
(drift di pochi pixel in espansione) — dettagli e tentativi già scartati
in §11. Nessuno dei due impedisce l'uso normale del pannello. **Via
libera per M3.**

### M3 — Casi speciali e polishing — **elenco fissato 2026-07-25**

Tidal lock su stella già chiuso in M2 (JNSQ aveva l'ecosistema di test
pronto, Moho + Grannus, nessun cfg Kopernicus creato ad hoc). Punti
restanti, nell'ordine dato dall'utente:

1. **Test fallback font — IN CORSO (2026-07-25)**: `SaUi.PrimaryFontName`
   impostato temporaneamente a un nome inesistente, build già deployata.
   Checklist e stato in `notes/test-m3.md`.
2. **Test linea di cambio data — IMPLEMENTATO (2026-07-25)**: la data locale
   torna sul corpo home, ma ridistribuita fra le due righe invece che
   duplicata. **UT resta data+ora completi** (test M3: la duplicazione
   originale era perché la vecchia riga sotto LOCAL TIME mostrava la
   STESSA data di UT senza mai dipendere dalla longitudine — non perché
   UT stesso non dovesse avere una data). **Sotto "LOCAL TIME · TZ±n"**: nuova data locale VERA,
   calcolata spostando UT dell'offset del fuso corrente
   (`UT + TimeZoneIndex · (solarDayLength/N)`) e passando il risultato a
   `PrintDateCompact(_, false)` — a differenza della vecchia riga (che
   mostrava semplicemente `PrintDateCompact(UT, false)`, cioè la stessa
   data di UT, mai dipendente dalla longitudine), questa varia col fuso e
   può differire di un giorno rispetto a UT vicino al confine opposto al
   meridiano 0 — è la linea di cambio data vera e propria, testabile
   anche sul corpo home. Approssimazione consapevole: usa il fuso in
   forma lineare (`k · L`), non la correzione equazione-del-tempo di
   `HomeClockCalibration` — per uno scarto massimo di qualche minuto su
   una grandezza a granularità di giorni, ininfluente tranne nei secondi
   esatti attorno alla mezzanotte. Su corpi non-home, invariato: il
   contatore Sol sostituisce la riga e attraversa già correttamente il
   confine di fuso equivalente (stesso meccanismo dal 2026-07-17).
   Checklist di verifica in `notes/test-m3.md`.
3. **Dial mini nella strip collassata — IMPLEMENTATO (2026-07-25)**:
   dettagli in §6.7. Checklist di verifica in `notes/test-m3.md`.
4. **Soglia quota superficie/suborbitale — IMPLEMENTATO (2026-07-28)**:
   NON più apoapsis ma **altitudine attuale del veicolo**
   (`vessel.altitude`, ASL) confrontata con una frazione del raggio del
   corpo — e SOLO quando la situazione stock è già `SUB_ORBITAL` (non
   sostituisce affatto il controllo di situazione esistente su
   LANDED/SPLASHED/PRELAUNCH/FLYING, lo estende):
   `situazione==SUB_ORBITAL && vessel.altitude <= body.Radius *
   soglia` → resta modalità Superficie (`SaModeSelector.IsLowSubOrbital`).
   Un'orbita bassa ma STABILE (situazione ORBITING) non è mai toccata da
   questa regola, qualunque sia la sua altitudine — solo traiettorie che
   non hanno ancora completato un'orbita. **Soglia regolabile** (go
   dell'utente 2026-07-28, non più un 0.25 fisso): slider
   `SaParams.surfaceAltitudeThresholdMultiplier` nei settings, 0.15-1.5
   (step 0.05), default 0.25 — stesso meccanismo di `uiScale` (§6.8/§6.10),
   scelto invece di un override manuale nel `.cfg` per restare coerente
   col resto delle impostazioni SA e non richiedere di editare file.

   **Eccezione "stellar dive" (IMPLEMENTATO 2026-07-28)**: il punto 4 ha
   esposto un caso che il resto del design non copriva — un volo
   suborbitale/atmosferico basso su una STELLA (raggiungibile sia dal
   nuovo ramo SUB_ORBITAL, sia dai rami preesistenti FLYING/LANDED se la
   stella ha un'atmosfera/superficie). Ora locale, fuso e dial non hanno
   senso (non c'è un sole "altrove" da cui derivarli — stesso problema
   già risolto per l'eccezione STAR-CENTRIC in Orbita), ma EXT TEMP e
   PRESSURE restano perfettamente validi. **Decisione architetturale**:
   NON un `SaMode` dedicato — override sui singoli campi dentro
   `SaMode.Surface`, chiave `r.BodyIsStar` (già esistente, stesso campo
   usato da STAR-CENTRIC). Motivo: la condizione di visibilità di EXT
   TEMP/PRESSURE è già `(Mode==Surface||Mode==TidalLock) &&
   BodyHasAtmosphere` — restando in `Surface` quella condizione torna
   vera automaticamente, senza toccarla; un `SaMode` nuovo avrebbe
   richiesto riscriverla (e ogni altro switch su `r.Mode`) solo per
   ottenere lo stesso risultato. Override applicati:
   - Clock main: etichetta d'allerta rossa **"STELLAR DIVE"**
     (`#LOC_SA_val_stellarDive`), sostituisce l'ora locale — stesso
     pattern cromatico di "TIDAL LOCK".
   - Clock sub: riga singola, riuso di `#LOC_SA_val_noLocalTime` (già
     usata da Tidal Lock) invece di una chiave nuova.
   - Fase testuale → "—"; day-progress/countdown alba-tramonto → vuoto;
     tick orario del timeline → "—".
   - Dial: l'arco pieno diventa **rosso/danger e fisso** (non vuoto come
     di notte — un arco vuoto comunicherebbe "buio", l'opposto di
     "avvolto dalla stella"); il punto-sole e il cursore della timeline
     sono nascosti. Mini-dial nella strip: nessun arco di riempimento da
     ricolorare lì, quindi è il TRACK RING stesso a diventare rosso, col
     punto-sole nascosto.
   - Riga SUN (EL/AZ): nascosta interamente (nuovo `sunRowGo`, stesso
     pattern di `extTempRowGo`) — "elevazione del sole" non ha senso
     quando il veicolo è sul/nel sole stesso. Trattamento "nascondi"
     (non "—") perché `BodyIsStar` è un fatto strutturale per-corpo,
     stabile per tutta la permanenza lì, non un caso limite numerico
     transitorio come i poli (punto 5) — stessa logica di EXT
     TEMP/PRESSURE.
   - FLUX, coordinate, bioma, gravità, HULL TEMP: **invariati**, restano
     dati fisicamente validi (e FLUX in particolare diventa un'ottima
     spia del pericolo).
   - Riga KSC: nessuna modifica necessaria — la condizione
     `Mode==Surface && IsHomeBody` è già naturalmente falsa (il corpo
     home non è mai la stella).

5. **Comportamento vicino ai poli — IMPLEMENTATO (2026-07-28)**: confermata
   la sola soglia `cos(lat)` già prevista in §5.5 (niente "circolo
   polare" separato — con tilt assiale zero non c'è un vero cambiamento
   astronomico a quella latitudine, solo il problema numerico che §5.5
   già isolava). Sopra la soglia diventano instabili, oltre ad
   azimut/longitudine, anche la RIGA ORA LOCALE (fuso/orario derivano
   dalla stessa longitudine) e il DIAL (l'arco giorno/notte usa lo stesso
   angolo orario). **Soglia**: `SaReadoutProvider.NearPoleLatitudeThresholdDeg
   = 87.5` (costante, non esposta nei settings — caso troppo di nicchia
   per uno slider dedicato, a differenza della soglia del punto 4).
   Partita da una stima puramente analitica (89°, `~cos(89°) ≈ 1°` di
   escursione di elevazione, modello tilt zero: `sin(elev) =
   cos(lat)·cos(H)`), **ritarata a 87.5° per osservazione empirica in
   gioco** (retest 2026-07-28): l'effetto "midnight sun" inizia a
   percepirsi visivamente un po' prima di quanto suggerisse il solo
   calcolo dell'ampiezza.

   **Etichette descrittive invece di "—" generico** (raffinamento
   2026-07-28, richiesta utente): un "—" nudo per un'intera riga/stato è
   meno informativo di una frase, a differenza di un singolo valore
   indefinito dentro una riga altrimenti normale (vedi AZ sotto, che
   resta "—"). Override, stesso pattern architetturale della stellar
   dive (§9 punto 4: campo dentro `SaMode.Surface`, non un `SaMode`
   nuovo, chiave `r.NearPole` — nuovo campo su `SaReadout`, calcolato una
   volta in `SaReadoutProvider.Build` invece di ripetere il confronto
   sulla latitudine in ogni punto della UI):
   - Clock main: **"POLAR ZONE"** (`#LOC_SA_val_polarZone`), colore
     **neutro** (`TextDim`, non `Danger`) — a differenza della stellar
     dive, questo non è pericoloso, solo inaffidabile.
   - Clock sub: riuso di `#LOC_SA_val_noLocalTime` (stesso riuso della
     stellar dive).
   - Fase sotto il dial: **"MIDNIGHT SUN"** (`#LOC_SA_val_midnightSun`)
     — descrive cosa succede davvero: il sole tecnicamente tramonta
     comunque (non è un vero giorno polare), ma l'escursione di
     elevazione è così piccola che non fa mai davvero buio.
   - Dial: arco vuoto (come di notte, NON pieno/rosso come la stellar
     dive — qui non c'è nulla di pericoloso da segnalare), punto-sole e
     cursore della timeline nascosti; la banda "day" della timeline resta
     invariata (già statica/decorativa in Superficie, non deriva mai
     dalla lunghezza reale del giorno). Tick orari → "—".
   - Riga SUN: **resta "—" solo sull'AZ** (non una frase) — è un singolo
     valore indefinito dentro una riga altrimenti normale, EL resta
     valido e mostrato (non esplode al polo, si schiaccia solo verso 0°).
   - Coordinate e resto del pannello: invariati — lat/lon restano ben
     definiti anche al polo esatto.
6. **Tempo medio su tutti i corpi + riga SOLAR TIME — IMPLEMENTATO
   (2026-07-28)**.

   **Rinominata `HomeClockCalibration` → `MeanTimeCalibration`**
   (era home-only, ora cache `Dictionary<CelestialBody, ...>` — stesso
   pattern chiave già usato da `PSystemManager.OrbitRendererDataCache`
   altrove nel codebase, CelestialBody è un singleton stabile per
   partita, sicuro come chiave). **LOCAL TIME diventa tempo medio su
   TUTTI i corpi** (non solo home, go esplicito dell'utente 2026-07-28)
   — rimossa la condizione `if (body.isHomeWorld)` in `BuildSurface`:
   prima, sui corpi non-home, LOCAL TIME leggeva il tempo apparente
   live (`SolarMath.DayFraction` sul centro fuso); quel valore live è
   ora esclusivo della nuova riga SOLAR TIME (alla longitudine esatta,
   non quantizzata).

   **Insidia fisica individuata prima di scrivere codice** (verifica
   punto-per-punto col utente): `EquationOfTimeSeconds` (estratta in
   `SolarMath.cs` come da piano, ora pubblica e generica) NON può usare
   `body.orbit.eccentricity` direttamente per un corpo qualsiasi — per
   una luna come Mun, `body.orbit` è l'orbita di Mun attorno a KERBIN,
   la cui eccentricità non ha nulla a che fare con l'equazione del
   tempo (governata dall'eccentricità di KERBIN attorno al Sole). Fix:
   nuovo helper privato `StarOrbitingAncestor(body, star)` risale la
   catena `referenceBody` fino al corpo che orbita direttamente la
   stella (già risolta da `StarResolver`, riuso diretto, nessuna nuova
   verifica sul decompilato) — per Mun risale a Kerbin e usa la sua
   eccentricità; per Kerbin stesso è un no-op (già il corpo che orbita
   la stella). `solarDayLength` nella formula resta invece quello del
   corpo LOCALE (la sua propria rotazione), solo il termine di
   eccentricità/anomalia media cambia sorgente.

   **Nuova riga "SOLAR TIME"**: non una data-row separata come MET, ma
   una **3ª riga del dial** (sotto "day 64%" / "↓ sunset 2h39m",
   architettura simile a Tidal Lock: override di contenuto, non un
   `SaMode` nuovo), dietro un nuovo toggle Impostazioni (default OFF,
   pattern di MET). Ora solare vera in `HH:MM:SS` (stessa scomposizione
   N-ore-locali di LOCAL TIME) alla longitudine ESATTA del veicolo, non
   al centro fuso — la differenza concettuale con LOCAL TIME, che resta
   quantizzato per fuso. Click **sull'intera area del dial** (nuovo
   `ClickCatcher` su `SaDial.Handle.labelsArea` — il dial non aveva
   click handling prima d'ora) cicla al secondo formato, **equazione
   del tempo** (formato "timer" locale proporzionato, non un `±mm:ss`
   fisso — corretto in retest 2026-07-28/29, vedi §3.7) — no-op se il
   toggle è spento, per non far cambiare una preferenza invisibile
   senza feedback. **Etichetta ciclabile insieme al valore** (bug UX
   corretto in retest 2026-07-30: un prefisso fisso "SOLAR" su
   entrambi i formati suggeriva due viste della stessa grandezza, come
   negli altri campi ciclabili del pannello — sono invece due
   grandezze concettualmente distinte, un orario legato alla
   longitudine esatta contro uno scarto valido per l'intero corpo,
   indipendente da qualunque longitudine) — **"LAT"** (Local Apparent
   Time, termine standard di gnomonica per questa stessa grandezza) per
   il formato Clock, **"EQT"** per l'equazione del tempo. Nascosta nelle
   eccezioni stellar dive/near-pole (stesso motivo di LOCAL TIME:
   longitudine inaffidabile o inesistente lì).

   **Aggiunta valutata e confermata durante l'implementazione**: con
   SOLAR TIME attivo, anche il countdown alba/tramonto (2ª riga)
   passa dal valore quantizzato per fuso a quello sulla longitudine
   esatta — più preciso, coerente col resto della riga. **Solo il
   countdown**: fase e arco del dial restano ancorati al fuso (coerenti
   con LOCAL TIME).

   **Artefatti da moto veloce (preoccupazione dell'utente, stesso
   problema del timer orbita)**: il countdown esatto-longitudine, a
   differenza di LOCAL TIME/Period, ha il suo **proprio percorso non
   throttled** in `SaWindow.Update()` (una volta per frame renderizzato,
   non 10 Hz) — `SaReadoutProvider.TryBuildSolarCountdownFast`, stesso
   pattern di `TryBuildOrbitTimerFast`. Giorno%/ora solare/equazione del
   tempo restano cachati dall'ultimo refresh throttled (non hanno
   bisogno di freschezza per-frame, solo il countdown ne beneficia
   davvero) — ricombinati ogni frame in `BuildSurfaceSubText`, unica
   fonte di verità condivisa fra il percorso throttled e quello fast.
7. **Refactoring per pubblicazione** — pulizia pre-release (non ancora
   dettagliato).
8. **Flusso multistar** (§4.3) — esplicitamente rimandato dall'utente,
   non affrontato finché il resto non è sviluppato.
9. **Meteo** (`notes/indagine-meteo.md`) — esplicitamente rimandato
   dall'utente, stessa ragione.

Restano da questa lista precedente, non ancora coperti sopra: soglie led
CommNet definitive (già chiuse in realtà, M0 §7 — l'utente le ha appena
confermate OK nel retest, nessun'azione residua); verifica Kronometer
completa (confermata OK nello stesso retest); README (nota font
Linux/macOS, da scrivere col resto del refactoring di rilascio, punto 7).
Checklist di test per M3 non ancora scritta (`notes/test-m3.md`) — verrà
creata mano a mano che ciascun punto viene implementato, come già fatto
per M1/M2.

### M3+ (oltre M3, non ancora pianificato in dettaglio)

Selettore Sol a 3 modalità per i corpi non-home (Universale/Milestone/
JPL-style, §3.4) — richiede persistenza ScenarioModule (Milestone) o
VesselModule (JPL-style); overlay fusi orari su mappa SCANsat (§3.4). Nessuna
delle due è bloccante per M1/M2/M3, restano annotate per quando il resto
della mod sarà stabile.

---

## 10. Decisioni chiuse (2026-07-17)

1. Ore di riferimento da `dateTimeFormatter` (armonia con la UI di gioco).
2. Fuso +0 centrato sulla longitudine 0 di ogni corpo (il KSC non a lon 0 è
   fatto accettato dalla community).
3. Sol da UT 0, per corpo, bypassando l'epoca Kronometer; "Sol dall'atterraggio"
   documentato come estensione futura.
4. Tidal lock = bloccato SULLA STELLA (flag + referenceBody stella + guardia
   solarDay degenere); lune locked sul pianeta → badge footer.
5. Superficie = LANDED/SPLASHED/PRELAUNCH/FLYING; resto = orbita; soglia quota
   per airless/suborbitale predisposta, tarata in M3.
6. Terminatore in km (+gradi), solo per lock sulla stella, bidirezionale,
   distanza lungo il parallelo.
7. Font Consolas via OS con fallback stock; test del fallback in M2; nota README.
8. ToolbarControl.
9. Tempo al prossimo evento in unità UT via `dateTimeFormatter`.
10. Led CommNet semaforico; soglie da verificare su convenzioni stock
    (placeholder 25% per il giallo).
11. Pressione zero → "VUOTO"; temperatura e pressione sempre visibili.
12. MET opzionale (default OFF) con ricompatto finestra.
13. Mockup approvati: estesi A/C/D + strip B1/B2/B3 (`notes/mockup-approvati.html`).

---

## 11. Problemi noti (cosmetici, non bloccanti, non in caccia attiva)

Bug reali, non urgenti, con almeno un tentativo di fix già provato e
scartato — registrati qui invece che rincorsi all'infinito. Se emerge
un'idea nuova (non solo una variazione dei tentativi già scartati),
riaprire la discussione prima di riprovare in codice.

### 11.1 Dial orbita — puntini residui nell'arco notturno

Nell'anello orbitale, 2-4 puntini chiari mobili restano visibili
nell'arco notturno (dove `orbitShadow` incontra `orbitTrack`
sottostante). Causa nota (§6.2): i due estremi APERTI di `orbitShadow`
non hanno un raccordo che chiuda la fessura ad ogni tick (il fix esiste
solo per i vertici interni). **Tentativo fatto e scartato
(2026-07-22)**: estendere il disco di raccordo anche ai due estremi —
elimina i puntini ma introduce dei "pallini" visibili ai bordi
dell'ombra, esteticamente peggiori dell'artefatto originale (il disco
rotondo sporge oltre il taglio netto atteso dell'arco). Serve un'idea
diversa dal semplice "disco anche qui" — es. un raccordo a forma di cuneo
invece che circolare, o generare `orbitShadow` con vertici sulla STESSA
griglia angolare di `orbitTrack` invece che su un intervallo continuo
indipendente (così i due estremi cadono esattamente su un vertice già
esistente di `orbitTrack`, nessuna fessura da chiudere). Non implementato.

### 11.2 Toggle strip — drift in espansione

Espandere (strip→esteso) fa scendere la finestra di pochi pixel invece
di tenere fisso il bordo superiore; collassare (esteso→strip) resta
corretto (§6.7). La formula di compensazione è simmetrica per
costruzione (verificato algebricamente) — il sospetto è un problema di
MISURA (`newHeight` letta troppo presto rispetto alla convergenza del
`ContentSizeFitter` annidato quando il contenuto cresce molto). **Tentativo
fatto e scartato (2026-07-22)**: doppia chiamata a
`ForceRebuildLayoutImmediate` prima di leggere l'altezza — non ha
risolto il drift e ha introdotto propri artefatti visivi. Causa non
identificata con certezza; serve indagare più a fondo (es. loggare
`oldHeight`/`newHeight` effettivi ad ogni toggle, in entrambe le
direzioni, per vedere se `newHeight` è davvero sottostimata come
ipotizzato) prima di riprovare un fix.
