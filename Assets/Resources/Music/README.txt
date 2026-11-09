Music (MusicManager). Drop audio files in these folders; any number, picked at random.

  Menu/                 main menu, pre-game screen, map editor
  General/              in play, mixed with the faction folder below
  Faction/<Faction>/    in play, on that faction's turn (folder name = the faction asset's name)
  Victory/              game over, a human player won (or AI-only game with a winner)
  Loss/                 game over, every human lost, or a draw
  Special/              only when a scenario trigger asks: MusicManager.PlaySpecial("clip name") / StopSpecial()

Files here are imported streamed (Editor/MusicImport.cs) so they are not all loaded into memory.
Fades and volume: Resources/GameSounds.asset, Music section.
