using UnityEngine;

namespace SituationalAwareness.Core
{
	/// <summary>
	/// Keeps SaParams.uiScale, a per-save slider because that is the only kind the
	/// stock settings dialog can host, in sync with the real player-global value
	/// in SaPersist. It polls rather than listening: the difficulty dialog assigns
	/// a new GameParameters on dismiss without firing any event.
	///
	/// Direction rule: a Game seen for the first time gets the global value pushed
	/// INTO its slider, so a save never overrides the player's choice; afterwards
	/// any difference is the player moving it, and flows back out.
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
