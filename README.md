# Stretch Switch

Petite app Windows pour jouer en résolution étirée sans y penser.
Quand un jeu se lance, elle :

- coupe le HDR,
- passe l'écran dans la résolution choisie (ex. 1440 x 1080 étiré),
- pousse la vibrance des couleurs (jusqu'à 400 %, toutes cartes graphiques),
- ferme les applis choisies (Chrome, Spotify…).

Quand le jeu se ferme, tout revient comme avant.

## Télécharger

**[⬇ StretchSwitch.zip (dernière version)](https://github.com/LuchoCoco/stretch-switch/releases/latest/download/StretchSwitch.zip)**

Dézippe, lance `StretchSwitch.exe`. Rien à installer (Windows 10 / 11).
Si Windows affiche « Windows a protégé votre ordinateur » : *Informations complémentaires* puis *Exécuter quand même*.

L'app se met à jour toute seule.

## Raccourcis

| Touches | Action |
|---|---|
| Ctrl+Alt+V | vibrance on / off |
| Ctrl+Alt+↑ / ↓ | vibrance +10 % / −10 % |
| Ctrl+Alt+R | tout remettre d'origine |

Le mode d'emploi complet est dans `LISEZ-MOI.txt`.

## Compiler

`build.bat` (utilise le compilateur C# fourni avec Windows). Publier une version : `.\publier.ps1 1.3.1 "notes"`.
