using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Keeps SaParams.uiScale (a per-save difficulty slider, the only kind
	/// of slider the stock settings dialog can host) equal to
	/// SaPersist.UiScale (the real, player-global value), in both directions.
	/// Bug fixed 2026-09-14 (user): the scale was a plain GameParameters
	/// field, so every save had its own and a fresh save came up at 1.0
	/// again.
	///
	/// Polling, not events, on purpose: the difficulty dialog assigns a new
	/// GameParameters on dismiss without firing anything (verified on the
	/// decompiled MiniSettings.OnDifficultyOptionsDismiss — only the outer
	/// settings dialog's Apply fires OnGameSettingsApplied, and Cancel there
	/// keeps the new parameters anyway), and the player can move the slider
	/// in scenes where no other SA addon is alive (space center, tracking
	/// station). One dictionary lookup and a float compare per frame, on a
	/// single object that lives across scenes.
	///
	/// Direction rule: a Game instance seen for the first time gets the
	/// global value pushed INTO its slider (a save is never allowed to
	/// override the player's choice); after that, any difference can only
	/// be the player moving the slider, so it flows OUT to the global value
	/// and is saved at once. KSP creates a new Game instance on every scene
	/// change, which is fine: the push is a no-op when the two already agree.
	/// </summary>
	[KSPAddon(KSPAddon.Startup.Instantly, true)]
	internal class SaUiScaleSync : MonoBehaviour
	{
		private Game syncedGame;

		private void Awake()
		{
			DontDestroyOnLoad(gameObject);
		}

		private void Update()
		{
			Game game = HighLogic.CurrentGame;
			if (game == null)
			{
				syncedGame = null;
				return;
			}
			if (game.Parameters == null) return;
			SaParams p = game.Parameters.CustomParams<SaParams>();
			if (p == null) return;

			SaPersist.EnsureLoaded();
			if (!ReferenceEquals(game, syncedGame))
			{
				p.uiScale = SaPersist.UiScale;
				syncedGame = game;
				return;
			}
			if (!Mathf.Approximately(p.uiScale, SaPersist.UiScale))
			{
				SaPersist.UiScale = Mathf.Clamp(p.uiScale, 0.5f, 2f);
				SaPersist.Save();
			}
		}
	}
}
