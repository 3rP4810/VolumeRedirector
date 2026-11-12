# Volume Redirector

Programma per Windows che fa agire i tasti multimediali **Volume Su**, **Volume Giù** e **Volume Muto** sul volume di **una sola app** (per esempio Spotify) invece che sul volume di sistema.

Un'icona nell'area di notifica (tray) tiene il programma sempre pronto, e una piccola flyout nello stile di Windows 11 mostra l'app, il suo livello e se è muta.

> 📷 **Segnaposto screenshot:** la flyout con icona, nome, altoparlante, barra e valore.
> 📷 **Segnaposto screenshot:** la finestra delle impostazioni.

---

## A cosa serve

Esempio pratico: stai guardando un video e ascolti musica in sottofondo con Spotify. Con Volume Redirector scegli Spotify come app collegata:

- **Volume Su / Giù** alzano e abbassano solo la musica, senza toccare il video.
- **Volume Muto** zittisce solo Spotify, mentre il resto del sistema continua a suonare.
- Quando Spotify è chiuso, i tasti tornano a regolare il volume di sistema, così non restano mai inutilizzabili.

---

## Caratteristiche

| Funzione | Cosa fa |
|---|---|
| **Scelta dell'app** | Dalla finestra delle impostazioni scegli fra le app che hanno una sessione audio attiva (le stesse che vedi nel Mixer volume di Windows). |
| **Volume Su / Giù** | Cambiano il volume dell'app scelta di un passo del 5% a ogni pressione, senza toccare quello di sistema. |
| **Volume Muto** | Muta e riattiva solo l'app scelta. Il muto cambia una volta sola per pressione, anche se tieni il tasto premuto. |
| **Flyout** | A ogni tasto gestito compare una scheda con icona e nome dell'app, altoparlante, barra e valore del livello. Segue il tema chiaro o scuro e il colore di accento di Windows. |
| **Ripiego su Windows** | Se non c'è nessuna app scelta, o l'app scelta non ha una sessione audio, i tasti passano a Windows (volume o muto di sistema). |
| **Avvisi dalla tray** | Se l'app scelta non è utilizzabile, un avviso a fumetto spiega perché i tasti agiscono sul sistema. Al massimo uno ogni 10 secondi. |
| **Avvio automatico con `--run`** | Parte nella tray senza finestra né avvisi: pensato per la cartella di avvio di Windows. |
| **Avvio manuale** | Con un doppio clic sull'exe si apre la finestra delle impostazioni. |
| **Istanza unica** | Può esserci una sola copia del programma per utente. Un secondo avvio manuale riporta in primo piano la finestra della copia già attiva. |
| **Avvia con Windows** | Una casella nelle impostazioni crea e rimuove da sola il collegamento nella cartella di avvio, con l'argomento `--run`. |
| **Icona nella tray** | Doppio clic per aprire le impostazioni; menù con **Impostazioni** ed **Esci**. |

---

## Requisiti

- **Windows 11.** La flyout è disegnata sul modello di quella di Windows 11 e usa il font *Segoe Fluent Icons*. Su Windows 10 il funzionamento non è stato verificato.
- **.NET 10** (applicazione `net10.0-windows`): serve il *.NET 10 Desktop Runtime* per eseguirla, l'*SDK .NET 10* per compilarla.
- Un dispositivo audio di riproduzione predefinito.

### Limiti noti

- Il programma controlla le sessioni audio del **dispositivo di riproduzione predefinito**.
- La flyout compare sul **monitor principale**. Con giochi a schermo intero esclusivo può non essere visibile (funziona con la modalità finestra senza bordi).
- Windows non consegna i tasti multimediali a un programma non elevato quando è in primo piano una finestra eseguita come amministratore. È un limite di Windows, non gestito dal programma.

---

## Installazione

### A) Usare l'eseguibile già compilato

1. Installa il **.NET 10 Desktop Runtime** se non è già presente.
2. Copia **tutta la cartella** con `VolumeRedirector.exe` e le sue DLL in un posto stabile, per esempio `C:\Programmi\VolumeRedirector\`.
3. Avvia `VolumeRedirector.exe` con un doppio clic: si apre la finestra delle impostazioni.
4. Facoltativo: spunta **Avvia con Windows** per farlo partire all'accesso (vedi [Avvio automatico](#avvio-automatico-con---run)).

> Se sposti la cartella dopo aver attivato "Avvia con Windows", il collegamento punta al vecchio percorso e la casella risulta vuota. Riapri l'app dal nuovo percorso e spunta di nuovo la casella.

### B) Compilare dal sorgente

Con la riga di comando, dalla cartella del progetto (quella con `VolumeRedirector.csproj`):

```powershell
dotnet build -c Release
```

L'eseguibile si trova in:

```
bin\Release\net10.0-windows\VolumeRedirector.exe
```

Per ottenere una cartella da copiare altrove:

```powershell
dotnet publish -c Release
```

Il risultato è in `bin\Release\net10.0-windows\publish\`.

Con Visual Studio: apri `VolumeRedirector.csproj` (serve una versione che supporti .NET 10), scegli la configurazione **Release** e usa *Compila > Compila soluzione*.

---

## Primo utilizzo

1. Avvia l'app di cui vuoi regolare il volume (per esempio Spotify) e **fai partire un brano**: l'app compare nella lista solo se ha una sessione audio.
2. Avvia Volume Redirector con un doppio clic. Si apre la finestra **Seleziona applicazione target**.
3. Seleziona l'app nella lista. Se non la vedi, premi **🔄 Aggiorna lista**.
4. Premi **✅ Seleziona**. Compare una conferma e la finestra si chiude. Il programma **resta attivo** nella tray.
5. Premi **Volume Su** o **Volume Giù**: cambia il volume dell'app scelta e compare la flyout.
6. Premi **Volume Muto**: l'app si zittisce e la flyout mostra l'altoparlante barrato. Premilo di nuovo per riattivarla.
7. Per cambiare app o rimuovere il collegamento, fai doppio clic sull'icona nella tray.

> Se non vedi l'icona, controlla il menù delle icone nascoste (la freccetta **^** nella barra delle applicazioni). Puoi trascinarla fuori per tenerla sempre visibile: è Windows a decidere dove metterla.

---

## Funzionamento

### Cosa fanno i tasti

Per "sessione audio" si intende un'app che compare nel Mixer volume di Windows.

| Situazione | Volume Su / Giù | Volume Muto | Flyout | Avviso dalla tray |
|---|---|---|---|---|
| **Nessuna app scelta** | Volume di sistema | Muto di sistema | No | No |
| **App scelta ma chiusa** | Volume di sistema | Muto di sistema | No | «*Nome* non è in esecuzione» |
| **App scelta aperta, senza sessione audio** (per esempio mai avviata la riproduzione) | Volume di sistema | Muto di sistema | No | «*Nome* è aperta ma non sta riproducendo audio, uso il volume di sistema» |
| **App scelta con sessione audio** | Regolano l'app di ±5% | Inverte il muto dell'app | Sì | No |
| **App scelta e muta** | **Su** toglie il muto e alza il volume. **Giù** lascia il muto e abbassa il livello. | Toglie il muto | Sì | No |

Altri dettagli:

- Quando i tasti passano a Windows compare la **flyout di sistema**, non la nostra, così non se ne vedono due.
- Gli avvisi sono al massimo **uno ogni 10 secondi**, uguali per i tasti volume e per il muto. Un clic sull'avviso apre la finestra delle impostazioni.
- Il livello mostrato nella flyout è quello del cursore dell'app nel **Mixer volume**, non il volume finale ottenuto combinandolo con quello di sistema. Con il sistema al 30% e l'app al 100%, la flyout mostra 100.

### Avvio manuale

Un doppio clic sull'exe, senza argomenti, apre la finestra delle impostazioni. Se l'app salvata non è in esecuzione, compare anche un avviso all'avvio. Qualsiasi argomento diverso da `--run` viene trattato come un avvio manuale.

### Avvio automatico con `--run`

Con `--run` (maiuscole e minuscole indifferenti) il programma parte **solo nella tray**, senza finestra e senza avvisi di avvio.

Puoi attivarlo in due modi:

**Con la casella** (consigliato): nella finestra delle impostazioni spunta **Avvia con Windows**. Il programma crea nella cartella di avvio il collegamento `VolumeRedirector.lnk` con destinazione l'exe seguito da `--run`. Togliendo la spunta il collegamento viene eliminato. La casella risulta spuntata solo se il collegamento esiste, punta a questo exe e ha `--run`.

**A mano:** premi `Win + R`, scrivi `shell:startup`, crea un collegamento all'exe e nel campo *Destinazione* aggiungi `--run`:

```
"C:\Programmi\VolumeRedirector\VolumeRedirector.exe" --run
```

### Una sola copia

Esiste una sola istanza del programma per utente:

- Un **secondo avvio manuale** non apre una seconda copia: chiede a quella già attiva di mostrare la finestra e si chiude.
- Un **secondo `--run`** si chiude subito, senza fare nulla.

### Uscire dal programma

- Chiudere la finestra delle impostazioni, anche dopo **Seleziona**, **non** chiude il programma: resta nella tray.
- Per chiuderlo davvero usa **Esci** dal menù dell'icona nella tray.

### Il muto resta attivo

Il programma **non toglie mai il muto in automatico**: né all'uscita, né se si chiude da solo, né con **Rimuovi collegamento**, né se scegli un'altra app. Se muti Spotify e poi esci, Spotify resta muta. Per riattivarla premi **Volume Su** finché il programma è attivo oppure usa il **Mixer volume** di Windows.

---

## Impostazioni e file

### Impostazioni

Il file delle impostazioni è:

```
%APPDATA%\VolumeRedirector\settings.json
```

Esempio:

```json
{
  "TargetProcessName": "spotify",
  "TargetDisplayName": "Spotify",
  "VolumeStep": 0.05,
  "MinimizeToTray": false
}
```

| Voce | Significato |
|---|---|
| `TargetProcessName` | Nome del processo dell'app scelta (per esempio `spotify`, `chrome`). Se vuoto, nessuna app è scelta. |
| `TargetDisplayName` | Nome mostrato nella flyout e negli avvisi. |
| `VolumeStep` | Passo del volume a ogni pressione, da 0.0 a 1.0. Predefinito `0.05` (5%). Viene riportato a `0.05` ogni volta che premi **Seleziona** o **Rimuovi collegamento**. |
| `MinimizeToTray` | Se `true`, il pulsante *Riduci a icona* della finestra la nasconde nella tray invece di lasciarla nella barra. Nell'interfaccia attuale non c'è una casella per cambiarlo, e **Seleziona** e **Rimuovi collegamento** lo riportano a `false`. |

Se il file manca o non si legge, il programma parte con le impostazioni predefinite (nessuna app scelta).

### Collegamento di avvio

Creato dalla casella **Avvia con Windows**:

```
%APPDATA%\Microsoft\Windows\Start Menu\Programs\Startup\VolumeRedirector.lnk
```

Si apre anche con `Win + R` e `shell:startup`.

---

## Risoluzione dei problemi

| Problema | Causa probabile | Rimedio |
|---|---|---|
| I tasti volume agiscono sempre sul sistema | Nessuna app scelta, oppure il programma non è in esecuzione | Cerca l'icona nella tray (anche fra quelle nascoste). Se non c'è, avvia il programma. Scegli un'app e premi **Seleziona**. |
| I tasti agiscono sul sistema e compare un avviso | L'app scelta è chiusa o non ha una sessione audio | Avvia l'app e fai partire l'audio. Se non ti serve più, scegline un'altra. |
| L'app non compare nella lista | Non ha una sessione audio attiva | Avvia la riproduzione e premi **🔄 Aggiorna lista**. |
| L'app è muta e non so perché | Hai premuto **Volume Muto** | Premi **Volume Su** o **Volume Muto**, oppure usa il Mixer volume di Windows. |
| Non vedo l'icona nella tray | Windows l'ha messa fra le icone nascoste | Apri la freccetta **^** e trascina l'icona fuori. |
| L'icona del programma è quella predefinita | La risorsa dell'icona non è stata trovata e viene usata l'icona dell'exe | Controlla l'icona impostata nel progetto e ricompila. |
| La flyout non compare | I tasti sono passati a Windows (nessuna app utilizzabile), il monitor principale non è quello che guardi, oppure sei in un gioco a schermo intero esclusivo | Controlla la tabella dei tasti. Usa la modalità finestra senza bordi. |
| Non vedo l'avviso | Al massimo uno ogni 10 secondi, oppure Windows è in modalità "Non disturbare" | Aspetta qualche secondo, oppure controlla il centro notifiche. |
| La casella "Avvia con Windows" è vuota dopo averla spuntata | Hai spostato l'exe, o il collegamento è stato cancellato | Riapri l'app dal percorso definitivo e spunta di nuovo la casella. |
| Errore creando il collegamento di avvio | La cartella di avvio non è scrivibile o manca il componente di Windows per i collegamenti | Leggi il messaggio mostrato. In alternativa crea il collegamento a mano con `shell:startup`. |

---

## Struttura del progetto

| File | Ruolo |
|---|---|
| `App.xaml.cs` | Avvio, riconoscimento di `--run`, icona nella tray, avvisi, decisione su cosa fare a ogni tasto, creazione della flyout. |
| `MainWindow.xaml` / `.cs` | Finestra delle impostazioni: lista delle app, **Seleziona**, **Rimuovi collegamento**, **Avvia con Windows**. |
| `VolumeFlyoutWindow.xaml` / `.cs` | Flyout: finestra trasparente, senza focus e non cliccabile, con tema, colore di accento, posizione e dissolvenze. |
| `Services/HotkeyService.cs` | Hook globale di tastiera (`WH_KEYBOARD_LL`) per Volume Su, Giù e Muto. |
| `Services/AudioService.cs` | Sessioni audio con NAudio: elenco delle app, ricerca della sessione, volume e muto. |
| `Services/SettingsService.cs` | Lettura e scrittura di `settings.json`, con copia in memoria. |
| `Services/SingleInstanceService.cs` | Istanza unica per utente e segnale "mostra la finestra". |
| `Services/StartupShortcutService.cs` | Creazione, controllo e rimozione del collegamento nella cartella di avvio. |
| `Models/AppSettings.cs` | Modello delle impostazioni. |

