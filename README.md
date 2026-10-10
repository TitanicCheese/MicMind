# MicMind

Ett litet Windows-program som får datorn att **alltid komma ihåg din mikrofon** (t.ex. Blue Snowball),
även när du drar ur den ibland.

## Hur det fungerar

```
Blue Snowball ──► MicMind ──► CABLE Input  ═══ virtuell kabel ═══►  CABLE Output ──► Windows / Discord / Teams / spel …
 (kan kopplas ur)                                                   (finns alltid kvar)
```

Windows och alla program använder en **virtuell mikrofon** ("CABLE Output") som aldrig försvinner.
MicMind skickar ljudet från din riktiga mikrofon in i den.

* **Snowball ur** → den virtuella mikrofonen finns kvar och ger tystnad. Inga program byter mikrofon.
* **Snowball i igen** → MicMind hittar den automatiskt inom någon sekund (även i en annan USB-port) och ljudet är tillbaka.
* Om Windows försöker byta standardmikrofon sätter MicMind tillbaka den virtuella (kan stängas av).

## Installation

1. **Installera VB-CABLE** (gratis virtuell ljudkabel): <https://vb-audio.com/Cable/>
   Packa upp, högerklicka `VBCABLE_Setup_x64.exe` → *Kör som administratör* → *Install Driver*. Starta om datorn.
   > En "riktig" virtuell mikrofon kräver en signerad drivrutin i Windows-kärnan – därför används VB-CABLE
   > som drivrutin och MicMind som den smarta delen.
2. **Hämta `MicMind.exe`**: under fliken **Actions** på GitHub → senaste körningen av *Build MicMind.exe* →
   artefakten **MicMind** (eller under **Releases** om en release finns). Lägg filen t.ex. i `C:\Program\MicMind\`.
3. **Starta `MicMind.exe`.** Inställningsfönstret öppnas första gången:
   * **Mikrofon att komma ihåg:** välj *Blue Snowball* (Snowball måste vara inkopplad första gången).
   * **Virtuell kabel:** välj *CABLE Input (VB-Audio Virtual Cable)*.
   * Låt *Håll den virtuella mikrofonen som standardmikrofon* och *Starta automatiskt* vara ikryssade.
   * Klicka **Spara**. Prata i mikrofonen – nivåmätaren ska röra sig.
4. Välj **CABLE Output** (eller "Standard") som mikrofon i Discord, Teams, OBS osv.

## Användning

MicMind ligger som en mikrofonikon i aktivitetsfältet (nere till höger, ev. under pilen ^):

| Färg   | Betyder |
|--------|---------|
| Grön   | Snowball är ansluten och ljudet går igenom |
| Orange | Snowball är urkopplad – väntar och ansluter automatiskt |
| Röd    | Inte inställt, eller VB-CABLE hittas inte |

Klicka på ikonen för inställningar, högerklicka för att avsluta.

## Tips

* Hörs du dåligt/för tyst? Justera nivån på Snowball under *Ljudinställningar → Inspelning → Blue Snowball → Egenskaper → Nivåer*.
  Ställ **inte** in programmen på Snowball direkt – då är problemet tillbaka.
* Inställningarna sparas i `%APPDATA%\MicMind\settings.json`.
* Avinstallera: avmarkera autostart i inställningarna, avsluta och radera `MicMind.exe`.

## Bygga själv

Kräver .NET 8 SDK på Windows:

```
dotnet publish src/MicMind/MicMind.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```
