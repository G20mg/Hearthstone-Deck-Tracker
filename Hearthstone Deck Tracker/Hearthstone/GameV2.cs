﻿#region

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Enums.Hearthstone;
using Hearthstone_Deck_Tracker.Hearthstone.Entities;
using Hearthstone_Deck_Tracker.Replay;
using Hearthstone_Deck_Tracker.Stats;
using Hearthstone_Deck_Tracker.Utility.Logging;
using Hearthstone_Deck_Tracker.Windows;
using MahApps.Metro.Controls.Dialogs;

#endregion

namespace Hearthstone_Deck_Tracker.Hearthstone
{
	public class GameV2 : IGame
	{
		public readonly List<long> IgnoredArenaDecks = new List<long>();
		private GameMode _currentGameMode = GameMode.None;

		private Mode _currentMode;

		public GameV2()
		{
			Player = new Player(this, true);
			Opponent = new Player(this, false);
			IsInMenu = true;
			OpponentSecrets = new OpponentSecrets(this);
			Reset();
		}

		public List<string> PowerLog { get; } = new List<string>();
		public Deck IgnoreIncorrectDeck { get; set; }
		public GameTime GameTime { get; } = new GameTime();
		public bool IsMinionInPlay => Entities.FirstOrDefault(x => (x.Value.IsInPlay && x.Value.IsMinion)).Value != null;

		public bool IsOpponentMinionInPlay
			=> Entities.FirstOrDefault(x => (x.Value.IsInPlay && x.Value.IsMinion && x.Value.IsControlledBy(Opponent.Id))).Value != null;

		public int OpponentMinionCount => Entities.Count(x => (x.Value.IsInPlay && x.Value.IsMinion && x.Value.IsControlledBy(Opponent.Id)));
		public int PlayerMinionCount => Entities.Count(x => (x.Value.IsInPlay && x.Value.IsMinion && x.Value.IsControlledBy(Player.Id)));

		public Player Player { get; set; }
		public Player Opponent { get; set; }
		public bool IsInMenu { get; set; }
		public bool IsUsingPremade { get; set; }
		public int OpponentSecretCount { get; set; }
		public bool IsRunning { get; set; }
		public Region CurrentRegion { get; set; }
		public GameStats CurrentGameStats { get; set; }
		public OpponentSecrets OpponentSecrets { get; set; }
		public List<Card> DrawnLastGame { get; set; }
		public Dictionary<int, Entity> Entities { get; } = new Dictionary<int, Entity>();
		public GameMetaData MetaData { get; } = new GameMetaData();
		internal List<Tuple<string, List<string>>> StoredPowerLogs { get; } = new List<Tuple<string, List<string>>>();
		internal Dictionary<int, string> StoredPlayerNames { get; } = new Dictionary<int, string>();
		internal GameStats StoredGameStats { get; set; }

		public Mode CurrentMode
		{
			get { return _currentMode; }
			set
			{
				_currentMode = value;
				Log.Info(value.ToString());
			}
		}

		public Format? CurrentFormat
		{
			get
			{
				if(CurrentGameMode != GameMode.Casual && CurrentGameMode != GameMode.Ranked)
					return null;
				if(DeckList.Instance.ActiveDeck?.IsArenaDeck ?? false)
					return null;
				if(!DeckList.Instance.ActiveDeck?.StandardViable ?? false)
					return Format.Wild;
				return Entities.Values.Where(x => !string.IsNullOrEmpty(x?.CardId) && !x.Info.Created && !string.IsNullOrEmpty(x.Card.Set))
							.Any(x => Helper.WildOnlySets.Contains(x.Card.Set)) ? Format.Wild : Format.Standard;
			}
		}

		public Mode PreviousMode { get; set; }

		public bool SavedReplay { get; set; }

		public Entity PlayerEntity => Entities.FirstOrDefault(x => x.Value.IsPlayer).Value;

		public Entity OpponentEntity => Entities.FirstOrDefault(x => x.Value.HasTag(GameTag.PLAYER_ID) && !x.Value.IsPlayer).Value;

		public Entity GameEntity => Entities.FirstOrDefault(x => x.Value?.Name == "GameEntity").Value;

		public bool IsMulliganDone
		{
			get
			{
				var player = Entities.FirstOrDefault(x => x.Value.IsPlayer);
				var opponent = Entities.FirstOrDefault(x => x.Value.HasTag(GameTag.PLAYER_ID) && !x.Value.IsPlayer);
				if(player.Value == null || opponent.Value == null)
					return false;
				return player.Value.GetTag(GameTag.MULLIGAN_STATE) == (int)Mulligan.DONE
					   && opponent.Value.GetTag(GameTag.MULLIGAN_STATE) == (int)Mulligan.DONE;
			}
		}

		private bool? _spectator;

		public bool Spectator => _spectator ?? (bool)(_spectator = HearthMirror.Reflection.IsSpectating());

		public GameMode CurrentGameMode
		{
			get
			{
				if(Spectator)
					return GameMode.Spectator;
				if(_currentGameMode == GameMode.None)
					_currentGameMode = HearthDbConverter.GetGameMode((GameType)HearthMirror.Reflection.GetGameType());
				return _currentGameMode;
			}
		}

		public void Reset(bool resetStats = true)
		{
			Log.Info("-------- Reset ---------");

			ReplayMaker.Reset();
			Player.Reset();
			Opponent.Reset();
			Entities.Clear();
			SavedReplay = false;
			OpponentSecretCount = 0;
			OpponentSecrets.ClearSecrets();
			_spectator = null;
			_currentGameMode = GameMode.None;
			if(!IsInMenu && resetStats)
				CurrentGameStats = new GameStats(GameResult.None, "", "") {PlayerName = "", OpponentName = "", Region = CurrentRegion};
			PowerLog.Clear();

			if(Core.Game != null && Core.Overlay != null)
			{
				Core.UpdatePlayerCards(true);
				Core.UpdateOpponentCards(true);
			}
		}

		public void StoreGameState()
		{
			if(string.IsNullOrEmpty(MetaData.GameId))
				return;
			Log.Info($"Storing PowerLog for gameId={MetaData.GameId}");
			StoredPowerLogs.Add(new Tuple<string, List<string>>(MetaData.GameId, new List<string>(PowerLog)));
			if(Player.Id != -1 && !StoredPlayerNames.ContainsKey(Player.Id))
				StoredPlayerNames.Add(Player.Id, Player.Name);
			if(Opponent.Id != -1 && !StoredPlayerNames.ContainsKey(Opponent.Id))
				StoredPlayerNames.Add(Opponent.Id, Opponent.Name);
			if(StoredGameStats == null)
				StoredGameStats = CurrentGameStats;
		}

		public string GetStoredPlayerName(int id)
		{
			string name;
			StoredPlayerNames.TryGetValue(id, out name);
			return name;
		}

		internal void ResetStoredGameState()
		{
			StoredPowerLogs.Clear();
			StoredPlayerNames.Clear();
			StoredGameStats = null;
		}

		#region Database - Obsolete

		[Obsolete("Use Hearthstone.Database.GetCardFromId", true)]
		public static Card GetCardFromId(string cardId) => Database.GetCardFromId(cardId);

		[Obsolete("Use Hearthstone.Database.GetCardFromName", true)]
		public static Card GetCardFromName(string name, bool localized = false) => Database.GetCardFromName(name, localized);

		[Obsolete("Use Hearthstone.Database.GetActualCards", true)]
		public static List<Card> GetActualCards() => Database.GetActualCards();

		[Obsolete("Use Hearthstone.Database.GetHeroNameFromId", true)]
		public static string GetHeroNameFromId(string id, bool returnIdIfNotFound = true)
			=> Database.GetHeroNameFromId(id, returnIdIfNotFound);

		[Obsolete("Use Hearthstone.Database.IsActualCard", true)]
		public static bool IsActualCard(Card card) => Database.IsActualCard(card);

		#endregion
	}
}