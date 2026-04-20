using System.Collections;
using UnityEngine;

namespace UnityWarehouseSceneHDRP
{
	public class ForkliftLight : MonoBehaviour
	{
		[SerializeField] private Renderer _light;
		[SerializeField] private Renderer _light2;
		[SerializeField] private GameObject _lineLight;
		[SerializeField] Transform _warningLight;
		[SerializeField] private float _rotateSpeed;

		[Header("Light Colors (URP Emission)")]
		[SerializeField] private Color _winkerColor    = new Color(1f, 0.6f, 0f);
		[SerializeField] private Color _brakelightColor = Color.red;
		[SerializeField] private Color _linelightColor  = Color.white;
		[SerializeField] private Color _patolampColor   = new Color(1f, 0.5f, 0f);

		private bool _winkerRightState;
		private bool _winkerLeftState;

		// ── Winker Right ───────────────────────────────────────────────────

		public void WinkerRight(bool isEnabled)
		{
			StopAllCoroutines();
			_winkerRightState = false;
			if (isEnabled)
				StartCoroutine(WinkerRightCoroutine());
			else
				SetEmission(_light, Color.black);
		}

		private IEnumerator WinkerRightCoroutine()
		{
			while (true)
			{
				yield return new WaitForSeconds(0.5f);
				_winkerRightState = !_winkerRightState;
				SetEmission(_light, _winkerRightState ? _winkerColor : Color.black);
			}
		}

		// ── Winker Left ────────────────────────────────────────────────────

		public void WinkerLeft(bool isEnabled)
		{
			StopAllCoroutines();
			_winkerLeftState = false;
			if (isEnabled)
				StartCoroutine(WinkerLeftCoroutine());
			else
				SetEmission(_light, Color.black);
		}

		private IEnumerator WinkerLeftCoroutine()
		{
			while (true)
			{
				yield return new WaitForSeconds(0.5f);
				_winkerLeftState = !_winkerLeftState;
				SetEmission(_light, _winkerLeftState ? _winkerColor : Color.black);
			}
		}

		// ── Line Light ─────────────────────────────────────────────────────

		public void LineLight(bool isEnabled)
		{
			SetEmission(_light, isEnabled ? _linelightColor : Color.black);
			_lineLight.SetActive(isEnabled);
		}

		// ── Brake Light ────────────────────────────────────────────────────

		public void Brakelight(bool isEnabled)
		{
			SetEmission(_light, isEnabled ? _brakelightColor : Color.black);
		}

		// ── Lifecycle ──────────────────────────────────────────────────────

		private void Awake()
		{
			SetEmission(_light2, _patolampColor);
		}

		private void Update()
		{
			_warningLight.Rotate(0, _rotateSpeed, 0);
		}

		// ── Helper ─────────────────────────────────────────────────────────

		private static void SetEmission(Renderer r, Color color)
		{
			if (r == null) return;
			var mat = r.material;
			if (color == Color.black)
			{
				mat.DisableKeyword("_EMISSION");
				mat.SetColor("_EmissionColor", Color.black);
			}
			else
			{
				mat.EnableKeyword("_EMISSION");
				mat.SetColor("_EmissionColor", color);
			}
		}
	}
}
