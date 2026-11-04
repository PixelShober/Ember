---
title: KI-Modul
description: Diesen Rechner für Wake-on-LAN freischalten, llama.cpp mit CUDA installieren, Modelle laden oder vom PC nehmen, llama-server starten und Pi anbinden.
---

![KI-Seite](../../../assets/screens/ai-de-light.png)

KI ist ein erweitertes Modul, genau wie Energie. Den Tab sieht nur, wer eine Rolle mit dem Modul `ai` hat (siehe [Berechtigungen](/Ember/handbuch/berechtigungen/)). <kbd>Strg</kbd> + <kbd>E</kbd> blendet ihn aus und wieder ein, Ember merkt sich das.

Die Freigabe ordnet nur die Oberfläche. Ember ist Open Source und läuft mit deinen Rechten, eine Sicherheitsgrenze ist das also nicht.

## Wake-on-LAN für diesen Rechner

Die erste Karte richtet diesen Rechner so ein, dass er sich aus dem ausgeschalteten Zustand (S5) wecken lässt. Andere Rechner weckt Ember nicht. Den Zielzustand hat Ember von einem Dell übernommen, auf dem das funktioniert: Dock mit Realtek-USB-Netzwerkkarte, dazu der Intel-I226-Anschluss im Gerät.

Für jeden kabelgebundenen Adapter prüft Ember:

| Einstellung | Ziel | Pflicht? |
|---|---|---|
| Bei Magic Packet aufwecken (`*WakeOnMagicPacket`) | an | ja |
| Wecken aus „Herunterfahren“ (`S5WakeOnLan` bei Realtek, `WakeOnMagicPacketFromS5` bei Intel, `EnablePME` bei älteren Intel-Treibern) | an | ja, falls der Treiber sie kennt |
| Gerät darf den Computer aktivieren (`powercfg /devicequery wake_armed`) | ja | ja |
| Bei Musterübereinstimmung aufwecken, Wake on link change | an | nein, Hinweis |
| WoL-Linkgeschwindigkeit | 10 Mbit/s zuerst | nein, Hinweis |

Dazu kommt der Windows-Schnellstart. Er muss aus sein, sonst fährt Windows nicht richtig herunter, und die Netzwerkkarte bekommt das Magic Packet nicht mit. Energiesparfunktionen wie EEE oder Green Ethernet lässt Ember in Ruhe, WOL braucht sie nicht.

„Zielzustand herstellen“ setzt alles mit einer einzigen Abfrage nach Adminrechten. Die Netzwerkverbindung ist dabei ein paar Sekunden weg. „Auf Standard zurücksetzen“ stellt die Treiber-Standardwerte wieder her, schaltet den Schnellstart ein und nimmt die Weck-Erlaubnis nur dort zurück, wo Ember sie selbst erteilt hat.

Die MAC-Adresse jedes Adapters lässt sich kopieren, für das Werkzeug, mit dem du den Rechner später weckst. Bei einem Dock mit MAC-Pass-Through ist das die Adresse, die Windows beim Dock-Adapter anzeigt.

:::caution[BIOS-Änderungen]
Das BIOS prüft Ember nicht. Bei Dell:

- „Wake on LAN“ einschalten
- „Wake on Dell USB-C Dock“ einschalten
- „MAC Address Pass-Through“ einschalten, wenn du über das Dock weckst
- „Deep Sleep Control“ ausschalten
:::

## Grafikkarte

Ember zeigt, was `nvidia-smi` meldet: Modell, Treiber, CUDA-Version, Speicher, Leistungsaufnahme und Temperatur. Ändern kann Ember hier nichts, Leistungsgrenzen bleiben Sache des Treibers.

## llama.cpp

„Neueste Version installieren“ holt das neueste Release von [ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp), das einen Windows-Build hat. Das „neueste“ Release von llama.cpp ist oft eines ohne Programmdateien, die Builds (`b12110` und so weiter) erscheinen als Vorabversionen. Ember sucht deshalb in den letzten 15 Releases. Die Variante:

- **CUDA**, wenn eine NVIDIA-Karte da ist. Ember nimmt den neuesten CUDA-Build innerhalb der Hauptversion, die dein Treiber kann: Ein Treiber mit CUDA 13.2 bekommt den 13.4-Build, ein Treiber mit CUDA 12.x den 12.4-Build. CUDA-13-Builds brauchen einen NVIDIA-Treiber ab Version 580.
- **Vulkan** für andere Grafikkarten.
- **CPU**, wenn es keine taugliche GPU gibt.

Bei CUDA lädt Ember zwei Zips, das Programm (`llama-bXXXXX-bin-win-cuda-13.x-x64.zip`) und die CUDA-Laufzeit (`cudart-llama-bin-win-cuda-13.x-x64.zip`). Beide prüft es gegen die SHA-256 von GitHub und entpackt sie in denselben Ordner. Danach läuft `llama-server --list-devices`: Bei CUDA muss dort `CUDA0` mit deiner Karte stehen, sonst bricht die Installation mit einem Hinweis zum Treiber ab.

Du kannst die Variante auch fest einstellen. Jede Version landet in einem eigenen Ordner unter `C:\Ember\llama.cpp`. Die zwei neuesten bleiben liegen, zwischen ihnen schaltest du mit „Verwenden“ um.

## Modelle

Eingebaut ist das Modell aus der Anleitung für 24 GB VRAM: **Qwen3.8-27B** als `Qwen3.8-27B-UD-Q4_K_XL.gguf` von Unsloth (etwa 17,6 GB). Es bringt Tool-Call-Parser für MCP mit und einen MTP-Kopf, der das Generieren beschleunigt.

Ein Modell kommt auf zwei Wegen in die Liste:

- **Download**: eine direkte Adresse, meist von Hugging Face (`…/resolve/main/datei.gguf`). Ember lädt nach `C:\Ember\Models`, über px, und setzt nach einem Abbruch fort. Steht eine SHA-256 dabei, prüft Ember sie.
- **Datei auf dem PC**: „Datei auf dem PC wählen“ nimmt eine GGUF-Datei, die schon irgendwo liegt, auch auf einem Netzlaufwerk. Ember lädt nichts herunter und löscht die Datei nie. Das Kreuz nimmt sie nur aus der Liste.

Die Team-Konfiguration kann Modelle vorgeben, mit Download-Adresse oder mit Pfad (siehe [Team-Konfiguration](/Ember/handbuch/team-konfiguration/#models)).

## llama-server

Du wählst Modell, Kontext-Profil und Port, der Rest folgt der Anleitung:

| Profil | Kontext | KV-Cache | MTP |
|---|---|---|---|
| standard | 96K (98.304 Token) | q8_0 | an |
| lang | 160K (163.840 Token) | q4_0 | an |
| max | 256K (262.144 Token) | q4_0 | aus |

Jedes Profil startet mit `-ngl 99` (alle Schichten auf die GPU), `-fa on`, `--jinja` und `--parallel 1`, dazu den KV-Cache des Profils (`-ctk`, `-ctv`). Was das Modell selbst braucht, hängt Ember an. Bei Qwen3.8 sind das die Sampling-Werte (`--temp 1.0 --top-p 0.95 --top-k 20 --min-p 0.0`), `reasoning_effort` medium und, wo das Profil es erlaubt, MTP (`--spec-type draft-mtp --spec-draft-n-max 2`). Unter „Weitere Argumente“ kommt dazu, was du sonst noch willst. Es steht am Ende und gewinnt deshalb.

Der Server lauscht nur auf `127.0.0.1`. Sobald das Modell geladen ist, zeigt Ember die Prüfung aus der Anleitung:

- **Grafikkarte**: welche GPU llama.cpp gefunden hat. Findet ein CUDA-Build keine, rechnet er still auf der CPU, oft weil die CUDA-Laufzeit fehlt. Ember warnt dann. Einen CUDA-Build ohne `cudart64_*.dll` startet Ember gar nicht erst.
- **Schichten auf der GPU**: steht im Log „offloaded X/Y layers to GPU“, zeigt Ember das an. Sind es nicht alle, rechnet ein Teil auf der CPU.
- **Tempo**: Token pro Sekunde beim Lesen des Prompts und beim Schreiben der Antwort, aus der letzten Anfrage.
- **Freier VRAM**: Mindestens 0,5 bis 1 GB sollten frei bleiben. Sonst ein kleineres Profil nehmen.

„Weboberfläche öffnen“ zeigt die eingebaute Chat-Seite von llama.cpp. Beim Beenden von Ember stoppt der Server mit.

## Pi

[Pi](https://www.npmjs.com/package/@earendil-works/pi-coding-agent) ist ein Coding-Agent, der den llama-server über die OpenAI-kompatible API anspricht.

- **Pi installieren** führt `npm install -g @earendil-works/pi-coding-agent` aus und danach `pi install npm:pi-mcp-adapter`, beides über px und mit den Firmenzertifikaten. Dafür braucht es Node.js, das du unter Software installierst.
- **Mit llama-server verbinden** trägt in `%USERPROFILE%\.pi\agent\models.json` den Provider `llama-cpp` ein: `http://127.0.0.1:<Port>/v1`, das Modell mit seinem Alias und `contextWindow` passend zum Profil. Andere Provider in der Datei bleiben, die alte Fassung liegt daneben als `models.json.bak`. Wechselst du das Profil, verbindest du noch einmal, damit Pi weiß, wann es den Verlauf zusammenfassen muss.

Gestartet wird im Projektordner mit `pi --provider llama-cpp --model qwen3.8-27b`. Die MCP-Server liest Pi aus der `.mcp.json` des Repos und aus `%USERPROFILE%\.pi\agent\mcp.json`. Schalte nur die ein, die du gerade brauchst, jede Tool-Beschreibung kostet Kontext.
