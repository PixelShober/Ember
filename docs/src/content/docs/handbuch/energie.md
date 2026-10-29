---
title: Energie
description: Energieprofile für das Schema „Ausbalanciert“, mit Werkseinstellung zum Zurücksetzen.
---

![Energie-Seite mit Ist, Ziel und Standard](../../../assets/screens/power-de-light.png)

Energie ist wie KI ein erweitertes Modul. Den Tab sieht nur, wer in der Team-Konfiguration als Admin steht (`admins`, siehe [Team-Konfiguration](/Ember/handbuch/team-konfiguration/#admins)). <kbd>Strg</kbd> + <kbd>E</kbd> blendet beide Tabs ein und aus.

## Warum nur „Ausbalanciert“

In vielen Firmen verbietet eine Gruppenrichtlinie die Energiepläne „Höchstleistung“ und „Ultimative Leistung“. Ember wechselt den Energiesparplan deshalb nie und legt auch keinen neuen an. Es ändert nur Werte innerhalb des erlaubten Schemas, standardmäßig „Ausbalanciert“. Die Team-Konfiguration kann das unter `power.allowedSchemes` festlegen.

Ember liest die Werte mit `powercfg /qh`. Das zeigt auch versteckte Einstellungen wie den Boost-Modus und die Energieeffizienz-Präferenz, ohne sie erst mit `/attributes` sichtbar zu machen. Die Ausgabe von powercfg ist übersetzt („Index der aktuellen Wechselstromeinstellung“ oder „Current AC Power Setting Index“). Ember wertet sie deshalb über GUIDs und Hex-Werte aus, nie über den Text.

## Die Profile

Jede Profilkarte zeigt pro Einstellung drei Werte, getrennt für Netzbetrieb und Akku:

- **Ist**: der aktuelle Wert. Grün, wenn er dem Ziel entspricht, sonst Orange.
- **Ziel**: was das Profil setzen will. Ein Punkt heißt, das Profil lässt diese Seite in Ruhe.
- **Standard**: die Werkseinstellung des Herstellers. Ember liest sie aus `DefaultPowerSchemeValues` in der Registry.

| Profil | Was es tut |
|---|---|
| KI-Leistung (Netzbetrieb) | Boost-Modus „Hoch (aggressiv)“, Energieeffizienz-Präferenz 0, minimaler Prozessorzustand 5 %, maximaler 100 %. Nur am Netzteil, der Akku bleibt unverändert. |
| Kein Standby am Netzteil | Kein Standby, kein Ruhezustand, Bildschirm aus nach 15 Minuten |
| Deckel & Netzschalter | Zuklappen am Netzteil: nichts tun. Netzschalter: herunterfahren. |

Bei KI-Leistung setzt Ember die Werte für alle Effizienzklassen des Prozessors. Auf Hybrid-CPUs mit P- und E-Kernen gibt es dieselbe Einstellung bis zu dreimal (`PERFEPP`, `PERFEPP1`, `PERFEPP2`). Wer nur die erste ändert, lässt die anderen Kerne auf dem alten Wert. Gibt es eine Variante auf dem Rechner nicht, überspringt Ember sie.

„Anwenden“ sichert vorher die aktuellen Werte. „Auf Standard zurücksetzen“ stellt die Werkseinstellung her, „Vorherigen Stand wiederherstellen“ den Stand vor der ersten Änderung.

Werte, die per Gruppenrichtlinie festgelegt sind, fasst Ember nicht an und markiert sie. Braucht eine Änderung Adminrechte, fragt Windows einmal nach. Brichst du ab, bietet Ember die Mail ans Service-Center mit den genauen `powercfg`-Befehlen an.

Die Profile kann die Team-Konfiguration ersetzen oder ergänzen, siehe [Team-Konfiguration](/Ember/handbuch/team-konfiguration/#energieprofile).

Wake-on-LAN für diesen Rechner steht jetzt im [KI-Tab](/Ember/handbuch/ki/#wake-on-lan-für-diesen-rechner).
