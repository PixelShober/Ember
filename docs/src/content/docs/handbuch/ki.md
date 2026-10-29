---
title: KI-Modul
description: Diesen Rechner für Wake-on-LAN freischalten, llama.cpp und Modelle verwalten, llama-server starten.
---

![KI-Seite](../../../assets/screens/ai-de-light.png)

KI ist ein erweitertes Modul, genau wie Energie. Beide Tabs sieht nur, wer in der Team-Konfiguration als Admin steht (`admins`). Ohne Admin-Liste sieht sie niemand. <kbd>Strg</kbd> + <kbd>E</kbd> blendet sie ein und aus, Ember merkt sich das.

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

:::caution[Das BIOS kann Ember nicht prüfen]
Bei Dell-Geräten müssen im BIOS „Wake on LAN“ und „Wake on Dell USB-C Dock“ eingeschaltet sein, je nach Gerät auch „MAC Address Pass-Through“. „Deep Sleep Control“ muss aus sein.
:::

## Grafikkarte

Ember zeigt, was `nvidia-smi` meldet: Modell, Treiber, CUDA-Version, Speicher, Leistungsaufnahme und Temperatur. Ändern kann Ember hier nichts, Leistungsgrenzen bleiben Sache des Treibers.

## llama.cpp

„Neueste Version installieren“ holt das aktuelle Release von [ggml-org/llama.cpp](https://github.com/ggml-org/llama.cpp) und wählt die passende Windows-Variante:

- CUDA, wenn eine NVIDIA-Karte da ist. Ember nimmt die höchste CUDA-Version, die dein Treiber unterstützt, und lädt die passenden CUDA-Laufzeitbibliotheken mit.
- Vulkan für andere Grafikkarten.
- CPU, wenn es keine taugliche GPU gibt.

Du kannst die Variante auch fest einstellen. Jede Version landet in einem eigenen Ordner unter `C:\Ember\llama.cpp`. Die zwei neuesten bleiben liegen, zwischen ihnen schaltest du mit „Verwenden“ um.

## Modelle

Modelle sind GGUF-Dateien mit einer direkten Download-Adresse, meist von Hugging Face (`…/resolve/main/datei.gguf`). Die Liste kommt aus der Team-Konfiguration und aus deinen eigenen Einträgen, gespeichert wird unter `C:\Ember\Models`. Downloads laufen über px und setzen nach einem Abbruch dort fort, wo sie aufgehört haben. Bei 20 GB macht das einen Unterschied. Steht eine SHA256-Prüfsumme dabei, prüft Ember sie nach dem Download.

## llama-server

Wähle ein heruntergeladenes Modell, einen Port und bei Bedarf weitere Argumente (Standard: `-ngl 99 -c 8192`). Der Server lauscht nur auf `127.0.0.1`. „Weboberfläche öffnen“ zeigt die eingebaute Chat-Seite von llama.cpp. Beim Beenden von Ember stoppt der Server mit.
