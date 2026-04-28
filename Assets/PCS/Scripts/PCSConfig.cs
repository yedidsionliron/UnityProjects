using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PCS
{
	[System.Serializable]
	public class PCSRailingData
	{
		public List<bool> enabledStates = new List<bool>();
	}

	[System.Serializable]
	public class PCSPart
	{
		public enum LengthMode { Stretch, Repeat };

		public GameObject prefab;
		public GameObject gameObject;
		public bool mirror;
		public Vector3 positionOffset;
		public Renderer[] renderers;
		public GameObject parent;
		public LengthMode lengthMode;
	};

	public class PCSConfig : MonoBehaviour
	{
		private const float SideFrameAllowance = 0.4f;
		private const float BaseBeltSurfaceWidth = 1.6f;
		private const float BeltColliderEndOverlap = 0.1f;

		public enum EditModes { None, Railings }

		struct PCSTransform{
			public Vector3 position;
			public Quaternion rotation;
			public Vector3 scale;
		};

		struct ConveyorGeometry
		{
			public float beltPitch;
			public float startCapLength;
			public float endCapLength;
			public float beltSurfaceWidth;
			public float beltWidthScale;
			public float verticalScale;
			public float beltElevation;
			public float runStartEdgeZ;
			public float runEndEdgeZ;
			public float firstTileCenterZ;
			public float beltCenterZ;
			public Vector3 runStart;
			public Vector3 runEnd;
			public Vector3 beltCenter;
			public Vector3 startCapAnchor;
			public Vector3 endCapAnchor;
			public float physicalLength;
		}
				
		public EditModes editMode;

		public PCSPart belt;
		public PCSPart startCap;
		public PCSPart endCap;
		public PCSPart railing;
		public PCSPart railingStartCap;
		public PCSPart railingEndCap;
		public PCSPart railingDoubleCap;
		public PCSPart internals;
		public GameObject physicsParent;

		public PCSRailingData[] railingData;
		private GameObject[][] railingTempParents = new GameObject[2][];
		private GameObject[] railingTempParentsStartCap;
		private GameObject[] railingTempParentsEndCap;

		public int length = 18;
		public float width = 2f;
		[FormerlySerializedAs("conveyorSupportHeight")]
		public float height = 1.05f;
		public bool internalsEnabled = false;
		public int internalsCount = 3;
		public float speed = 0.6f;

		[Header("Conveyor Supports")]
		[Tooltip("ConveyorSupport prefab to place under both ends of the conveyor.")]
		public GameObject conveyorSupportPrefab;
		[Tooltip("Scale applied to each support. Default matches Conveyor_Long1 (13, 13, 91).")]
		public Vector3 conveyorSupportScale = new Vector3(13f, 13f, 91f);
		[Tooltip("Slope angle in degrees. Positive = front elevated, negative = back elevated.")]
		public float conveyorSlopeAngle = 0f;
		public Color32 colour = new Color32(50, 50, 50 , 255);

		public PCSConveyor pcsC;
		public PCSsingulator pcsS;
		public bool singulatorMode = false;

		public bool settingsImported;
		
		public GameObject railingEditCollidersParent;
		public Dictionary<Collider, int> railingEditCollidersSideIndex;
		public Dictionary<Collider, int> railingEditCollidersRailingIndex;
		private List<Collider> visibleColliders;
		private List<Collider> railingColliders;

		private PCSTransform parentTransform;
		private GameObject _conveyorBody;
		private GameObject _supportsParent;
		private ConveyorGeometry _geometry;

		public List<GameObject> conveyorChildren;// = new List<GameObject>();

		public bool ready = false;

		public PCSUVScroller[] uvS = new PCSUVScroller[4];

		public float conveyorSupportHeight
		{
			get => height;
			set => height = value;
		}
		
#if UNITY_EDITOR
		private void Reset() => ApplyDefaultSupportPrefab();
		private void OnValidate() => ApplyDefaultSupportPrefab();

		private void ApplyDefaultSupportPrefab()
		{
			if (conveyorSupportPrefab == null)
			{
				conveyorSupportPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
					"Assets/LastMileAssets/Models/ConveyorSupport.fbx");
			}
		}
#endif

		public void CreatePCS()
		{
			deleteAllColliders();

			if (settingsImported)
			{
				CheckRailingData();
				visibleColliders = new List<Collider>();
				railingColliders = new List<Collider>();

				if (conveyorChildren != null)
				{
					foreach (GameObject obj in conveyorChildren)
					{
						DestroyImmediate(obj);
					}
				}

			

				Initialise();
				InstantiateObjects();
				CombineMeshes();
				InstantiateMaterials();
				CreatePhysicsComponenets();


				foreach (Collider c in visibleColliders)
				{
					c.hideFlags = HideFlags.HideInInspector | HideFlags.NotEditable;
				}

				// Railing colliders go on physicsParent (kinematic RB) so they reliably block packages.
				foreach (Collider c in railingColliders)
				{
					Collider newC = physicsParent.AddDuplicateCollider(c);
					newC.hideFlags = HideFlags.HideInInspector | HideFlags.NotEditable;
					DestroyImmediate(c);
				}

				ApplyTransform();

				conveyorChildren = new List<GameObject>();
				conveyorChildren.Add(_conveyorBody);
				conveyorChildren.Add(_supportsParent);

			}

		}

		private float GetBeltSurfaceWidth()
		{
			return Mathf.Max(0.01f, width - GetSideFrameAllowance());
		}

		private float GetBeltWidthScale()
		{
			return GetWidthScaleForSurfaceWidth(GetBeltSurfaceWidth());
		}

		public float GetSideFrameAllowance()
		{
			return SideFrameAllowance;
		}

		public float GetBaseBeltSurfaceWidth()
		{
			return BaseBeltSurfaceWidth;
		}

		public float GetWidthScaleForSurfaceWidth(float surfaceWidth)
		{
			return Mathf.Max(0.01f, surfaceWidth) / GetBaseBeltSurfaceWidth();
		}

		public float GetHeightForBeltTop(float beltTopHeight, float surfaceWidth)
		{
			return Mathf.Max(0.01f, beltTopHeight);
		}

		public float GetSupportHeightForBeltTop(float beltTopHeight, float surfaceWidth)
		{
			return GetHeightForBeltTop(beltTopHeight, surfaceWidth);
		}

		public float GetGroundToBeltTopHeight()
		{
			return Mathf.Max(0.01f, height);
		}

		private static Bounds GetCombinedRendererLocalBounds(Transform root)
		{
			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
			Bounds combined = new Bounds(Vector3.zero, Vector3.zero);
			bool hasBounds = false;
			Matrix4x4 worldToLocal = root.worldToLocalMatrix;
			foreach (Renderer r in renderers)
			{
				Bounds b = r.bounds;
				Vector3 c = b.center;
				Vector3 e = b.extents;
				for (int xi = -1; xi <= 1; xi += 2)
				for (int yi = -1; yi <= 1; yi += 2)
				for (int zi = -1; zi <= 1; zi += 2)
				{
					Vector3 corner = c + Vector3.Scale(e, new Vector3(xi, yi, zi));
					Vector3 localCorner = worldToLocal.MultiplyPoint3x4(corner);
					if (!hasBounds)
					{
						combined = new Bounds(localCorner, Vector3.zero);
						hasBounds = true;
					}
					else
					{
						combined.Encapsulate(localCorner);
					}
				}
			}

			return combined;
		}

		private static bool TryGetCombinedRendererWorldBounds(Transform root, out Bounds combined)
		{
			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
			if (renderers == null || renderers.Length == 0)
			{
				combined = default;
				return false;
			}

			combined = renderers[0].bounds;
			for (int i = 1; i < renderers.Length; i++)
			{
				combined.Encapsulate(renderers[i].bounds);
			}

			return true;
		}

		private static bool TryGetCombinedRendererBoundsInSpace(Transform root, Transform reference, out Bounds combined)
		{
			Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
			if (renderers == null || renderers.Length == 0)
			{
				combined = default;
				return false;
			}

			combined = default;
			bool hasBounds = false;
			Matrix4x4 worldToLocal = reference.worldToLocalMatrix;
			for (int i = 0; i < renderers.Length; i++)
			{
				Bounds b = renderers[i].bounds;
				Vector3 c = b.center;
				Vector3 e = b.extents;
				for (int xi = -1; xi <= 1; xi += 2)
				for (int yi = -1; yi <= 1; yi += 2)
				for (int zi = -1; zi <= 1; zi += 2)
				{
					Vector3 corner = c + Vector3.Scale(e, new Vector3(xi, yi, zi));
					Vector3 localCorner = worldToLocal.MultiplyPoint3x4(corner);
					if (!hasBounds)
					{
						combined = new Bounds(localCorner, Vector3.zero);
						hasBounds = true;
					}
					else
					{
						combined.Encapsulate(localCorner);
					}
				}
			}

			return hasBounds;
		}

		private static void AlignVisualBounds(
			GameObject instance,
			Transform parent,
			Vector3 targetLocalPosition,
			bool alignCenterX,
			bool alignBottomY,
			bool alignCenterZ)
		{
			instance.transform.localPosition = targetLocalPosition;
			if (!TryGetCombinedRendererBoundsInSpace(instance.transform, parent, out Bounds bounds)) return;

			Vector3 delta = Vector3.zero;
			if (alignCenterX)
				delta.x = targetLocalPosition.x - bounds.center.x;
			if (alignBottomY)
				delta.y = targetLocalPosition.y - bounds.min.y;
			if (alignCenterZ)
				delta.z = targetLocalPosition.z - bounds.center.z;
			if (delta.sqrMagnitude > 0.00000001f)
				instance.transform.localPosition += delta;
		}

		private static float GetBaseVisualHeight(GameObject prefab)
		{
			if (prefab == null) return 0f;
			return GetCombinedRendererLocalBounds(prefab.transform).size.y;
		}

		private static void PreserveVisualHeight(GameObject instance, Transform parent, float targetHeight)
		{
			if (instance == null || parent == null || targetHeight <= 0.0001f) return;
			if (!TryGetCombinedRendererBoundsInSpace(instance.transform, parent, out Bounds bounds)) return;
			if (bounds.size.y <= 0.0001f) return;

			Vector3 scale = instance.transform.localScale;
			scale.y *= targetHeight / bounds.size.y;
			instance.transform.localScale = scale;
		}

		private void BuildGeometry(float beltPrefabLength, float startCapPrefabLength, float endCapPrefabLength)
		{
			_geometry = new ConveyorGeometry
			{
				beltPitch = beltPrefabLength,
				startCapLength = startCapPrefabLength,
				endCapLength = endCapPrefabLength,
				beltSurfaceWidth = GetBeltSurfaceWidth(),
				beltWidthScale = GetWidthScaleForSurfaceWidth(GetBeltSurfaceWidth()),
				verticalScale = GetVerticalScale(),
				beltElevation = GetGroundToBeltTopHeight(),
				runStartEdgeZ = startCapPrefabLength,
				runEndEdgeZ = startCapPrefabLength + beltPrefabLength * length
			};
			_geometry.firstTileCenterZ = _geometry.runStartEdgeZ + (_geometry.beltPitch * 0.5f);
			_geometry.beltCenterZ = (_geometry.runStartEdgeZ + _geometry.runEndEdgeZ) * 0.5f;
			_geometry.runStart = new Vector3(0f, 0f, _geometry.firstTileCenterZ);
			_geometry.runEnd = new Vector3(0f, 0f, _geometry.runEndEdgeZ - (_geometry.beltPitch * 0.5f));
			_geometry.beltCenter = new Vector3(0f, 0f, _geometry.beltCenterZ);
			_geometry.startCapAnchor = new Vector3(0f, 0f, _geometry.startCapLength * 0.5f);
			_geometry.endCapAnchor = new Vector3(0f, 0f, _geometry.runEndEdgeZ + (_geometry.endCapLength * 0.5f));
			_geometry.physicalLength = _geometry.startCapLength + (_geometry.beltPitch * length) + _geometry.endCapLength;
		}

		public float GetVerticalScale()
		{
			return 1f;
		}

		public float GetSupportHalfHeight()
		{
			return GetGroundToBeltTopHeight() * 0.5f;
		}

		private static int GetDominantScaleAxis(Transform t, Vector3 worldDirection)
		{
			Vector3 dir = worldDirection.normalized;
			float x = Mathf.Abs(Vector3.Dot(t.TransformDirection(Vector3.right).normalized, dir));
			float y = Mathf.Abs(Vector3.Dot(t.TransformDirection(Vector3.up).normalized, dir));
			float z = Mathf.Abs(Vector3.Dot(t.TransformDirection(Vector3.forward).normalized, dir));

			if (x >= y && x >= z) return 0;
			if (y >= z) return 1;
			return 2;
		}

		private static float GetAxis(Vector3 value, int axis)
		{
			switch (axis)
			{
				case 0: return value.x;
				case 1: return value.y;
				default: return value.z;
			}
		}

		private static Vector3 SetAxis(Vector3 value, int axis, float axisValue)
		{
			switch (axis)
			{
				case 0: value.x = axisValue; break;
				case 1: value.y = axisValue; break;
				default: value.z = axisValue; break;
			}

			return value;
		}

		private void FitSupportScale(GameObject support, float targetWidth, float targetHeight)
		{
			if (support == null) return;

			if (!TryGetCombinedRendererWorldBounds(support.transform, out Bounds supportBounds)) return;

			Vector3 scale = support.transform.localScale;

			int widthAxis = GetDominantScaleAxis(support.transform, Vector3.right);
			float measuredWidth = supportBounds.size.x;
			if (measuredWidth > 0.0001f)
			{
				scale = SetAxis(scale, widthAxis, GetAxis(scale, widthAxis) * (targetWidth / measuredWidth));
				support.transform.localScale = scale;
				if (!TryGetCombinedRendererWorldBounds(support.transform, out supportBounds)) return;
			}

			int heightAxis = GetDominantScaleAxis(support.transform, Vector3.up);
			float measuredHeight = supportBounds.size.y;
			if (measuredHeight > 0.0001f)
			{
				scale = SetAxis(scale, heightAxis, GetAxis(scale, heightAxis) * (targetHeight / measuredHeight));
				support.transform.localScale = scale;
			}
		}

		public void CheckRailingData()
		{
			if (railingData == null || railingData.Length != 2 || railingData[0] == null || railingData[1] == null)
			{
				railingData = new PCSRailingData[2];

				railingData[0] = new PCSRailingData();
				railingData[1] = new PCSRailingData();
			}

			for (int i = 0; i < 2; i++)
			{
				if (railingData[i].enabledStates == null)
					railingData[i].enabledStates = new List<bool>();

				if (railingData[i].enabledStates.Count != length + 2)
				{

					for (int j = railingData[i].enabledStates.Count; j < length + 2; j++)
					{
						if (railingData[i].enabledStates.Count > 0)
							railingData[i].enabledStates.Add(railingData[i].enabledStates[railingData[i].enabledStates.Count - 1]);
						else
							railingData[i].enabledStates.Add(true);
					}
					while (railingData[i].enabledStates.Count > length + 2)
					{
						railingData[i].enabledStates.RemoveAt(length + 2);
					};
				}


			}
		}

		void Initialise()
		{
			uvS = new PCSUVScroller[4];	

			//--------------------Initialise transform---------------------
			parentTransform.position = transform.position;
			parentTransform.rotation = transform.rotation;
			parentTransform.scale = transform.localScale;
			//-------------------------------------------------------------

			//--------------------Initialise internals---------------------
			if (internalsEnabled && internalsCount < 2)
				internalsCount = 2;
			//-------------------------------------------------------------



			//-----Get prefab widths and renderers, set prefab offsets-----//-----------------------------------------------------------------------
			float beltPrefabWidth, startCapPrefabWidth, endCapPrefabWidth;

			belt.renderers = new Renderer[1];
			belt.renderers[0] = belt.prefab.GetComponent<Renderer>();

			railing.renderers = new Renderer[railing.prefab.transform.childCount];
			for (int i = 0; i < railing.renderers.Length; i++)
			{
				railing.renderers[i] = railing.prefab.transform.GetChild(i).GetComponent<MeshRenderer>();
			}

			railingStartCap.renderers = new Renderer[railingStartCap.prefab.transform.childCount];
			for (int i = 0; i < railingStartCap.renderers.Length; i++)
			{
				railingStartCap.renderers[i] = railingStartCap.prefab.transform.GetChild(i).GetComponent<MeshRenderer>();
			}

			railingEndCap.renderers = new Renderer[railingEndCap.prefab.transform.childCount];
			for (int i = 0; i < railingEndCap.renderers.Length; i++)
			{
				railingEndCap.renderers[i] = railingEndCap.prefab.transform.GetChild(i).GetComponent<MeshRenderer>();
			}

			internals.renderers = new Renderer[1];
			internals.renderers[0] = internals.prefab.GetComponent<Renderer>();

			beltPrefabWidth = GetCombinedRendererLocalBounds(belt.prefab.transform).size.z;
			startCapPrefabWidth = GetCombinedRendererLocalBounds(startCap.prefab.transform).size.z;
			endCapPrefabWidth = GetCombinedRendererLocalBounds(endCap.prefab.transform).size.z;
			BuildGeometry(beltPrefabWidth, startCapPrefabWidth, endCapPrefabWidth);

			belt.positionOffset = new Vector3(0, 0, beltPrefabWidth);
			startCap.positionOffset = new Vector3(0, 0, startCapPrefabWidth);
			endCap.positionOffset = new Vector3(0, 0, endCapPrefabWidth);
			//--------------------------------------------------------------------------------------------------------------------------------------


			//--------------------Create parent objects--------------------
			_conveyorBody = new GameObject("Conveyor Body");
			_conveyorBody.transform.parent = transform;
			_conveyorBody.transform.localPosition = Vector3.up * _geometry.beltElevation;
			_conveyorBody.transform.localRotation = Quaternion.identity;
			_conveyorBody.hideFlags = HideFlags.HideInHierarchy;

			belt.parent = new GameObject("Belt");
			belt.parent.transform.parent = _conveyorBody.transform;
			belt.parent.transform.localPosition = _geometry.beltCenter;
			belt.parent.hideFlags = HideFlags.HideInHierarchy;
			uvS[2] = belt.parent.AddComponent<PCSUVScroller>();
			uvS[2].speed = speed / 0.2f;



			railing.parent = new GameObject("Railing");
			railing.parent.transform.parent = _conveyorBody.transform;
			railing.parent.transform.localPosition = _geometry.beltCenter;
			railing.parent.hideFlags = HideFlags.HideInHierarchy;
			if (editMode == EditModes.Railings)
				railing.parent.SetActive(false);


			for (int j = 0; j < railing.prefab.transform.childCount; j++)
			{
				GameObject child = new GameObject(railing.prefab.transform.GetChild(j).name);
				child.transform.parent = railing.parent.transform;
				child.transform.localPosition = Vector3.zero;
				child.hideFlags = HideFlags.HideInHierarchy;
			}

			startCap.parent = new GameObject("Start Cap");
			startCap.parent.transform.parent = _conveyorBody.transform;
			startCap.parent.transform.localPosition = _geometry.startCapAnchor;
			startCap.parent.hideFlags = HideFlags.HideInHierarchy;

			railingStartCap.parent = new GameObject("Railing Start Cap");
			railingStartCap.parent.transform.parent = startCap.parent.transform;
			railingStartCap.parent.transform.localPosition = Vector3.zero;
			railingStartCap.parent.hideFlags = HideFlags.HideInHierarchy;
			if (editMode == EditModes.Railings)
				railingStartCap.parent.SetActive(false);

			for (int j = 0; j < railingStartCap.prefab.transform.childCount; j++)
			{
				GameObject child = new GameObject(railingStartCap.prefab.transform.GetChild(j).name);
				child.transform.parent = railingStartCap.parent.transform;
				child.transform.localPosition = Vector3.zero;
				child.hideFlags = HideFlags.HideInHierarchy;
			}

			endCap.parent = new GameObject("End Cap");
			endCap.parent.transform.parent = _conveyorBody.transform;
			endCap.parent.transform.localPosition = _geometry.endCapAnchor;
			endCap.parent.hideFlags = HideFlags.HideInHierarchy;

			railingEndCap.parent = new GameObject("Railing End Cap");
			railingEndCap.parent.transform.parent = endCap.parent.transform;
			railingEndCap.parent.transform.localPosition = Vector3.zero;
			railingEndCap.parent.hideFlags = HideFlags.HideInHierarchy;
			if (editMode == EditModes.Railings)
				railingEndCap.parent.SetActive(false);

			for (int j = 0; j < railingEndCap.prefab.transform.childCount; j++)
			{
				GameObject child = new GameObject(railingEndCap.prefab.transform.GetChild(j).name);
				child.transform.parent = railingEndCap.parent.transform;
				child.transform.localPosition = Vector3.zero;
				child.hideFlags = HideFlags.HideInHierarchy;
			}

			internals.parent = new GameObject("Internals");
			internals.parent.transform.parent = _conveyorBody.transform;
			internals.parent.transform.localPosition = Vector3.zero; //PCSUtils.GetBeltTopCenter(startCap.positionOffset, belt.positionOffset, length);
			internals.parent.hideFlags = HideFlags.HideInHierarchy;
			uvS[3] = internals.parent.AddComponent<PCSUVScroller>();
			uvS[3].speed = speed / (0.18f*Mathf.PI);

			physicsParent = new GameObject("Physics");
			physicsParent.transform.parent = _conveyorBody.transform;
			physicsParent.transform.localPosition = Vector3.zero;
			physicsParent.hideFlags = HideFlags.HideInHierarchy;

			_supportsParent = new GameObject("Conveyor Supports");
			_supportsParent.transform.parent = transform;
			_supportsParent.transform.localPosition = Vector3.zero;
			_supportsParent.hideFlags = HideFlags.HideInHierarchy;
			//-------------------------------------------------------------
		}

		void InstantiateObjects()
		{
			//Set start point for instantiating objects
			Vector3 instantiatePosition = _geometry.runStart;


			//Instantiate belt start cap
			startCap.gameObject = InstantiateCap(startCap.prefab, startCap.parent, startCap.mirror, "Belt Start Cap");
			startCap.gameObject.hideFlags = HideFlags.HideInHierarchy;
			uvS[0] = startCap.gameObject.AddComponent<PCSUVScroller>();
			uvS[0].speed = speed / 0.2f;


			//-----------------Initialse railing variables-----------------
			GameObject[][] railings = new GameObject[2][];
			int[] railingCount = GetRailingCount();
			railings[0] = new GameObject[railingCount[0]];
			railings[1] = new GameObject[railingCount[1]];
			int[] currentRailing = { 0, 0 };
			bool[] newR = { false, false };
			bool[] newMR = { false, false };

			//----------------Instantiate railing start cap----------------
			railingTempParentsStartCap = new GameObject[2];
			for (int i = 0; i < 2; i++)
			{
				if (railingData[i].enabledStates[0])
					railingTempParentsStartCap[i] = InstantiateRailingPiece(i, 0, instantiatePosition);
			}
			//-------------------------------------------------------------

			//Create railing parents
			CreateRailingParents(railingCount);
			//-------------------------------------------------------------


			//----------------Instantiate belt and railings----------------//---------------------------------------------------------------------------------
			if (belt.lengthMode == PCSPart.LengthMode.Stretch)
			{
				belt.gameObject = Instantiate(belt.prefab, belt.parent.transform);
				belt.gameObject.transform.localScale = Vector3.Scale(belt.gameObject.transform.localScale, new Vector3(GetBeltWidthScale(), GetVerticalScale(), length));
				if (belt.mirror)
					belt.gameObject.transform.localScale = Vector3.Scale(belt.gameObject.transform.localScale, new Vector3(1, 1, -1));
				PreserveVisualHeight(belt.gameObject, belt.parent.transform, GetBaseVisualHeight(belt.prefab));
				AlignVisualBounds(belt.gameObject, belt.parent.transform, Vector3.zero, true, true, true);
			}

			if (railing.lengthMode == PCSPart.LengthMode.Stretch)
			{
				Vector3 startInstPos = instantiatePosition;
				int[] pieceLength = { 1, 1 };
				GameObject[] middlePart = new GameObject[2];
				bool[] incrementCurrentRailing = { false, false };

				for (int i = 0; i < length+2; i++)
				{
					for (int j = 0; j < 2; j++)
					{
						//First part of railing
						if (railingData[j].enabledStates[i] && !newR[j])
						{

							if (i > 0 && i <= length)
							{
								railings[j][currentRailing[j]] = InstantiateRailingPiece(j, i, instantiatePosition);

								for (int k = 0; k < railing.prefab.transform.childCount; k++)
								{
									railings[j][currentRailing[j]].transform.GetChild(0).parent = railingTempParents[j][currentRailing[j]].transform.GetChild(k);
								}
								GameObject.DestroyImmediate(railings[j][currentRailing[j]]);
								incrementCurrentRailing[j] = true;
							}

							newR[j] = true;
						}
						//Middle part of railing
						else if (railingData[j].enabledStates[i] && (i <= length && railingData[j].enabledStates[i + 1]) && newR[j])// && railingData[j].enabledStates[i2])
						{
							if (!newMR[j])
							{
								if (i > 0 && i <= length)
								{
									railings[j][currentRailing[j]] = InstantiateRailingPiece(j, i, instantiatePosition);
									middlePart[j] = railings[j][currentRailing[j]];
									incrementCurrentRailing[j] = true;
								}

								newMR[j] = true;
							}
							else
								pieceLength[j]++;
						}
						//End part of railing
						else if (railingData[j].enabledStates[i] && newR[j] && (i <= length && !railingData[j].enabledStates[i + 1]))
						{
							if (i > 0 && i <= length)
							{
								railings[j][currentRailing[j]] = InstantiateRailingPiece(j, i, instantiatePosition);

								for (int k = 0; k < railing.prefab.transform.childCount; k++)
								{
									railings[j][currentRailing[j]].transform.GetChild(0).parent = railingTempParents[j][currentRailing[j]].transform.GetChild(k);
								}
								GameObject.DestroyImmediate(railings[j][currentRailing[j]]);
								incrementCurrentRailing[j] = true;
							}
						}
						//Next after end part of railing
						else if ((!railingData[j].enabledStates[i] || i > length) && newR[j])
						{
							if (newMR[j])
							{
								middlePart[j].transform.localScale = Vector3.Scale(middlePart[j].transform.localScale, new Vector3(1, 1, pieceLength[j]));

								for (int k = 0; k < railing.prefab.transform.childCount; k++)
								{
									MeshFilter mf = middlePart[j].transform.GetChild(0).gameObject.GetComponent<MeshFilter>();
									mf.sharedMesh = Instantiate(mf.sharedMesh);

									//Debug.Log(k + ", " + pieceLength[j]);
									middlePart[j].transform.GetChild(0).gameObject.ScaleUVs(new Vector2(pieceLength[j], 1));
									//Debug.Log(middlePart[j].transform.GetChild(0).gameObject.GetComponent<MeshRenderer>().sharedMaterial.mainTextureScale);

									middlePart[j].transform.GetChild(0).parent = railingTempParents[j][currentRailing[j]].transform.GetChild(k);
								}

								GameObject.DestroyImmediate(middlePart[j]);

								middlePart[j] = null;

							}

							pieceLength[j] = 1;
							if(incrementCurrentRailing[j])
								currentRailing[j]++;
							newR[j] = false;
							newMR[j] = false;
						}
							
					}

					if (i > 0 && i <= length)
						instantiatePosition += belt.positionOffset;
				}

				if (belt.lengthMode == PCSPart.LengthMode.Repeat)
					instantiatePosition = startInstPos;
			}

			for (int i = 0; i < length; i++)
			{
				if (belt.lengthMode == PCSPart.LengthMode.Repeat)
				{
					//----------------------Instantiate belt-----------------------
					belt.gameObject = Instantiate(belt.prefab, belt.parent.transform);
					if (belt.mirror)
						belt.gameObject.transform.localScale = Vector3.Scale(belt.gameObject.transform.localScale, new Vector3(1, 1, -1));
					AlignVisualBounds(
						belt.gameObject,
						belt.parent.transform,
						instantiatePosition - belt.parent.transform.localPosition,
						true,
						true,
						true);
					//-------------------------------------------------------------
				}


				//---------------------Instantiate railings--------------------
				if (railing.lengthMode == PCSPart.LengthMode.Repeat)
				{
					for (int j = 0; j < 2; j++)
					{
						if (railingData[j].enabledStates[i + 1])
						{
							railings[j][currentRailing[j]] = InstantiateRailingPiece(j, i + 1, instantiatePosition);

							for (int k = 0; k < railing.prefab.transform.childCount; k++)
							{
								railings[j][currentRailing[j]].transform.GetChild(0).parent = railingTempParents[j][currentRailing[j]].transform.GetChild(k);
							}
							GameObject.DestroyImmediate(railings[j][currentRailing[j]]);
							newR[j] = true;
						}
						else if (newR[j])
						{
							currentRailing[j]++;
							newR[j] = false;
						}
					}
				}
				//-------------------------------------------------------------


				//Increase instantiate position
				if (railing.lengthMode == PCSPart.LengthMode.Repeat || belt.lengthMode == PCSPart.LengthMode.Repeat)
					instantiatePosition += belt.positionOffset;
			}
			//------------------------------------------------------------------------------------------------------------------------------------------------


			//Instantiate belt end cap
			endCap.gameObject = InstantiateCap(endCap.prefab, endCap.parent, endCap.mirror, "Belt End Cap");
			endCap.gameObject.hideFlags = HideFlags.HideInHierarchy;
			uvS[1] = endCap.gameObject.AddComponent<PCSUVScroller>();
			uvS[1].speed = speed / 0.2f;

			//-----------------Instantiate railing end cap-----------------
			railingTempParentsEndCap = new GameObject[2];
			for (int i = 0; i < 2; i++)
			{
				if (railingData[i].enabledStates[railingData[i].enabledStates.Count - 1])
					railingTempParentsEndCap[i] = InstantiateRailingPiece(i, railingData[i].enabledStates.Count - 1, instantiatePosition);

			}
			//-------------------------------------------------------------


			//-------------------Spawn conveyor supports-------------------
			SpawnConveyorSupports();
			//-------------------------------------------------------------

			//--------------------Instantiate internals--------------------
			if (internalsEnabled)
			{
				GameObject internalPiece = Instantiate(internals.prefab);
				internalPiece.transform.parent = internals.parent.transform;
				internalPiece.transform.localScale = Vector3.Scale(internalPiece.transform.localScale, new Vector3(GetBeltWidthScale(), GetVerticalScale(), 1));
				if (internals.mirror)
					internalPiece.transform.localScale = Vector3.Scale(internalPiece.transform.localScale, new Vector3(1, 1, -1));
				PreserveVisualHeight(internalPiece, internals.parent.transform, GetBaseVisualHeight(internals.prefab));
				AlignVisualBounds(internalPiece, internals.parent.transform, _geometry.runStart, true, true, true);

				

				if (internalsCount > 2)
				{
						float dist = internals.positionOffset.z + belt.positionOffset.z * length; //Vector3.Distance(PCSUtils.GetStartCapTopEdge(startCap.positionOffset), PCSUtils.GetEndCapTopEdge(startCap.positionOffset, belt.positionOffset, length));
					//float gap = dist / (internalsCount - 2);
					//dist -= gap;
					float sep = dist / (internalsCount-1);
					for (int i = 1; i < internalsCount; i++)
					{
						internalPiece = Instantiate(internals.prefab);
						internalPiece.transform.parent = internals.parent.transform;
						internalPiece.transform.localScale = Vector3.Scale(internalPiece.transform.localScale, new Vector3(GetBeltWidthScale(), GetVerticalScale(), 1));
						//internalPiece.transform.localPosition += 
						if (internals.mirror)
							internalPiece.transform.localScale = Vector3.Scale(internalPiece.transform.localScale, new Vector3(1, 1, -1));
						PreserveVisualHeight(internalPiece, internals.parent.transform, GetBaseVisualHeight(internals.prefab));
						AlignVisualBounds(internalPiece, internals.parent.transform, _geometry.runStart + new Vector3(0, 0, sep * i), true, true, true);
					}
				}
				else
				{
					internalPiece = Instantiate(internals.prefab);
					internalPiece.transform.parent = internals.parent.transform;
					internalPiece.transform.localScale = Vector3.Scale(internalPiece.transform.localScale, new Vector3(GetBeltWidthScale(), GetVerticalScale(), 1));
					if (!internals.mirror)
						internalPiece.transform.localScale = Vector3.Scale(internalPiece.transform.localScale, new Vector3(1, 1, -1));
					PreserveVisualHeight(internalPiece, internals.parent.transform, GetBaseVisualHeight(internals.prefab));
					AlignVisualBounds(internalPiece, internals.parent.transform, _geometry.runEnd, true, true, true);
				}
			}


			//-------------------------------------------------------------

		}

		void SpawnConveyorSupports()
		{
			if (conveyorSupportPrefab == null) return;

			float angleRad = conveyorSlopeAngle * Mathf.Deg2Rad;
			float startZ = _geometry.runStartEdgeZ;
			float endZ   = _geometry.runEndEdgeZ;

			// Positive angle elevates endZ (larger Z) due to Unity X-rotation convention.
			// Front support (i=0) is assigned to endZ so it is the elevated end for positive angle.
			// Back support  (i=1) is assigned to startZ.
			float[] bodyZ     = { endZ,   startZ  };
			string[] names    = { "ConveyorSupport", "ConveyorSupport (1)" };

			for (int i = 0; i < 2; i++)
			{
				float capZ = bodyZ[i] * Mathf.Cos(angleRad);

				// Instantiate with the reference scale, then fit the support to the
				// current conveyor width and required height.
				GameObject support = Instantiate(conveyorSupportPrefab);
				support.name = names[i];
				support.transform.parent = _supportsParent.transform;
				support.transform.localRotation = Quaternion.Euler(-90f, 0f, -90f);
				support.transform.localScale = conveyorSupportScale;
				support.transform.localPosition = Vector3.zero;
				float capY = _geometry.beltElevation + bodyZ[i] * Mathf.Sin(angleRad);
				float targetHeight = capY;
				FitSupportScale(support, GetBeltSurfaceWidth(), targetHeight);

				// Measure world-space Z depth after fitting.
				// A vertical support of depth D at slope angle θ would pierce (D/2)*tan(θ)
				// above the angled belt surface, blocking packages. Subtract as clearance.
				float clearance = 0f;
				Bounds supportBounds = GetCombinedRendererLocalBounds(support.transform);
				if (Mathf.Abs(angleRad) > 0.0001f)
					clearance = (supportBounds.size.z * 0.5f) * Mathf.Abs(Mathf.Tan(angleRad));

				// Belt-end Y after slope rotation, pulled back by clearance so the support
				// top sits flush below the angled belt surface rather than piercing it.
				capY -= clearance;

				AlignVisualBounds(support, _supportsParent.transform, new Vector3(0f, 0f, capZ), true, true, true);

				foreach (MeshFilter mf in support.GetComponentsInChildren<MeshFilter>())
				{
					MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
					mc.convex = true;
				}
			}
		}

		GameObject InstantiateCap(GameObject capPrefab, GameObject capParent, bool mirrorCap, string name)
		{
			GameObject cap = Instantiate(capPrefab, capParent.transform);
			cap.name = name;
			cap.transform.localScale = Vector3.Scale(cap.transform.localScale, new Vector3(GetBeltWidthScale(), GetVerticalScale(), 1));
			if (mirrorCap)
				cap.transform.localScale = Vector3.Scale(cap.transform.localScale, new Vector3(1, 1, -1));
			PreserveVisualHeight(cap, capParent.transform, GetBaseVisualHeight(capPrefab));
			AlignVisualBounds(cap, capParent.transform, Vector3.zero, true, true, true);

			return cap;
		}

		GameObject InstantiateRailingPiece(int side, int index, Vector3 instantiatePosition)
		{
			instantiatePosition = instantiatePosition + new Vector3(side == 0 ?  (GetBeltSurfaceWidth() / 2f) + 0.1f : -((GetBeltSurfaceWidth() / 2f) + 0.1f), 0, 0);

			GameObject railingPiece;
			Transform parentTransform = _conveyorBody.transform;

			//Start Cap
			if (index == 0)
			{
				if (railingData[side].enabledStates[index + 1])
				{
					railingPiece = Instantiate(railingStartCap.prefab, parentTransform);

					if (railingStartCap.mirror)
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
					else
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
					AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
				}
				else
				{
					railingPiece = Instantiate(railingDoubleCap.prefab, parentTransform);

					if (railingStartCap.mirror)
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
					else
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
					AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
				}
			}
			//End Cap
			else if (index == railingData[side].enabledStates.Count - 1)
			{
				if (railingData[side].enabledStates[index - 1])
				{
					railingPiece = Instantiate(railingEndCap.prefab, parentTransform);

					if (railingEndCap.mirror)
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
					else
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
					AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
				}
				else
				{
					railingPiece = Instantiate(railingDoubleCap.prefab, parentTransform);

					if (railingEndCap.mirror)
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
					else
						railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
					AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
				}
			}
			//Regular railingPiece - double cap
			else if (!railingData[side].enabledStates[index - 1] && !railingData[side].enabledStates[index + 1])
			{
				railingPiece = Instantiate(railingDoubleCap.prefab, parentTransform);

				if (railingDoubleCap.mirror)
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
				else
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
				AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
			}
			//Regular railingPiece - start cap
			else if (!railingData[side].enabledStates[index - 1])
			{
				railingPiece = Instantiate(railingStartCap.prefab, parentTransform);

				if (railingStartCap.mirror)
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
				else
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
				AlignVisualBounds(
					railingPiece,
					parentTransform,
					instantiatePosition + Vector3.forward * railingStartCap.renderers[1].bounds.size.z,
					true,
					true,
					true);
			}
			//Regular railingPiece - end cap
			else if (!railingData[side].enabledStates[index + 1])
			{
				railingPiece = Instantiate(railingEndCap.prefab, parentTransform);

				if (railingEndCap.mirror)
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
				else
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
				AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
			}
			//Regular railingPiece - middle piece
			else
			{
				railingPiece = Instantiate(railing.prefab, parentTransform);

				if (railing.mirror)
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, -1));
				else
					railingPiece.transform.localScale = Vector3.Scale(railingPiece.transform.localScale, new Vector3(side == 0 ? 1 : -1, 1, 1));
				AlignVisualBounds(railingPiece, parentTransform, instantiatePosition, true, true, true);
			}

			return railingPiece;
		}

		void CombineMeshes()
		{
			//Merge belt
			belt.parent.CombineChildMeshes(belt.renderers[0].sharedMaterial);

			//Merge railing, part 1 - combine same children (on same side)
			for (int i = 0; i < 2; i++)
			{
				for (int j = 0; j < railingTempParents[i].Length; j++)
				{
					for (int k = 0; k < railingTempParents[i][j].transform.childCount; k++)
						railingTempParents[i][j].transform.GetChild(k).gameObject.CombineChildMeshes(railing.renderers[k].sharedMaterial);
				}
			}

			if (editMode == EditModes.None)
			{
				//Create railing colliders
				CreateColliders();
			}

			//Merge railing, part 2 - move colliders to parent
			for (int i = 0; i < 2; i++)
			{
				for (int j = 0; j < railingTempParents[i].Length; j++)
				{
					for (int k = 0; k < railing.prefab.transform.childCount; k++)
					{
						if (editMode == EditModes.None)
						{
							BoxCollider oldCollider = railingTempParents[i][j].transform.GetChild(0).GetComponent<BoxCollider>();
							railingColliders.Add(railing.parent.AddDuplicateCollider(oldCollider));
							BoxCollider.DestroyImmediate(oldCollider);
						}

						railingTempParents[i][j].transform.GetChild(0).parent = railing.parent.transform.GetChild(k);
					}

					GameObject.DestroyImmediate(railingTempParents[i][j]);
				}
			}


			//Merge railing, part 3 - merge same children (both sides)
			for (int i = 0; i < railing.parent.transform.childCount; i++)
			{
				railing.parent.transform.GetChild(i).gameObject.CombineChildMeshes(railing.renderers[i].sharedMaterial);
			}


			//Merge railing start cap, part 1 - move colliders to parent
			for (int i = 0; i < 2; i++)
			{
				if (railingData[i].enabledStates[0])
				{
					for (int j = 0; j < railingStartCap.prefab.transform.childCount; j++)
					{
						if (editMode == EditModes.None)
						{
							BoxCollider oldCollider = railingTempParentsStartCap[i].transform.GetChild(0).GetComponent<BoxCollider>();
							railingColliders.Add(railingStartCap.parent.AddDuplicateCollider(oldCollider));
							BoxCollider.DestroyImmediate(oldCollider);
						}

						railingTempParentsStartCap[i].transform.GetChild(0).parent = railingStartCap.parent.transform.GetChild(j);
					}
					GameObject.DestroyImmediate(railingTempParentsStartCap[i]);
				}
			}


			//Merge railing start cap, part 2 - merge same children (both sides)
			for (int i = 0; i < railingStartCap.prefab.transform.childCount; i++)
			{
				railingStartCap.parent.transform.GetChild(i).gameObject.CombineChildMeshes(railingStartCap.renderers[i].sharedMaterial);
			}

			
			//Merge railing end cap, part 1 - move colliders to parent
			for (int i = 0; i < 2; i++)
			{
				if (railingData[i].enabledStates[length + 1])
				{
					for (int j = 0; j < railingEndCap.prefab.transform.childCount; j++)
					{
						if (editMode == EditModes.None)
						{
							BoxCollider oldCollider = railingTempParentsEndCap[i].transform.GetChild(0).GetComponent<BoxCollider>();
							railingColliders.Add(railingEndCap.parent.AddDuplicateCollider(oldCollider));
							BoxCollider.DestroyImmediate(oldCollider);
						}

						railingTempParentsEndCap[i].transform.GetChild(0).parent = railingEndCap.parent.transform.GetChild(j);
					}
					GameObject.DestroyImmediate(railingTempParentsEndCap[i]);
				}
			}

			
			//Merge railing end cap, part 2 - merge same children (both sides)
			for (int i = 0; i < railingEndCap.prefab.transform.childCount; i++)
			{
				railingEndCap.parent.transform.GetChild(i).gameObject.CombineChildMeshes(railingEndCap.renderers[i].sharedMaterial);
			}

			//Merge internals
			internals.parent.CombineChildMeshes(internals.renderers[0].sharedMaterial);
		}

		void CreateColliders()
		{
			//Railing colliders
			for (int i = 0; i < railing.prefab.transform.childCount; i++)
			{
				for (int j = 0; j < railingTempParents[0].Length; j++)
				{
					AddFittedBoxCollider(railingTempParents[0][j].transform.GetChild(i).gameObject);
				}

				for (int j = 0; j < railingTempParents[1].Length; j++)
				{
					AddFittedBoxCollider(railingTempParents[1][j].transform.GetChild(i).gameObject);
				}
			}

			//Railing start cap colliders
			for (int i = 0; i < railingStartCap.prefab.transform.childCount; i++)
			{
				if (railingData[0].enabledStates[0])
				{
					if (railingTempParentsStartCap[0].transform.localScale != Vector3.one)
						railingTempParentsStartCap[0].FixScale(railingStartCap.renderers);
					AddFittedBoxCollider(railingTempParentsStartCap[0].transform.GetChild(i).gameObject);
				}

				if (railingData[1].enabledStates[0])
				{
					if (railingTempParentsStartCap[1].transform.localScale != Vector3.one)
						railingTempParentsStartCap[1].FixScale(railingStartCap.renderers);
					AddFittedBoxCollider(railingTempParentsStartCap[1].transform.GetChild(i).gameObject);
				}
			}

			//Railing end cap colliders
			for (int i = 0; i < railingEndCap.prefab.transform.childCount; i++)
			{
				if (railingData[0].enabledStates[railingData[0].enabledStates.Count - 1])
				{
					if (railingTempParentsEndCap[0].transform.localScale != Vector3.one)
						railingTempParentsEndCap[0].FixScale(railingEndCap.renderers);
					AddFittedBoxCollider(railingTempParentsEndCap[0].transform.GetChild(i).gameObject);
				}

				if (railingData[1].enabledStates[railingData[1].enabledStates.Count - 1])
				{
					if (railingTempParentsEndCap[1].transform.localScale != Vector3.one)
						railingTempParentsEndCap[1].FixScale(railingEndCap.renderers);
					AddFittedBoxCollider(railingTempParentsEndCap[1].transform.GetChild(i).gameObject);
				}
			}
		}

		static void AddFittedBoxCollider(GameObject go)
		{
			// AddComponent<BoxCollider>() via script does not auto-fit to the mesh --
			// it creates a default 1x1x1 collider. Explicitly size from Renderer bounds.
			MeshFilter mf = go.GetComponent<MeshFilter>();
			BoxCollider bc = go.AddComponent<BoxCollider>();
			if (mf == null || mf.sharedMesh == null) return;
			// Mesh.bounds is always valid immediately after CombineChildMeshes; Renderer.bounds may not be.
			bc.center = mf.sharedMesh.bounds.center;
			bc.size   = mf.sharedMesh.bounds.size;
		}

	void CreatePhysicsComponenets()
		{
			if (TryGetCombinedRendererBoundsInSpace(belt.parent.transform, physicsParent.transform, out Bounds beltBounds))
			{
				float fullLength = Mathf.Max(beltBounds.size.z, _geometry.physicalLength);
				float colliderLength = fullLength + (2f * BeltColliderEndOverlap);

				BoxCollider beltCollider = physicsParent.AddComponent<BoxCollider>();
				beltCollider.size = new Vector3(
					Mathf.Max(0.001f, beltBounds.size.x),
					Mathf.Max(0.001f, beltBounds.size.y),
					Mathf.Max(0.001f, colliderLength));
				beltCollider.center = new Vector3(beltBounds.center.x, beltBounds.center.y, fullLength * 0.5f);
				visibleColliders.Add(beltCollider);
			}

			Rigidbody coveyorRB = physicsParent.AddComponent<Rigidbody>();
			coveyorRB.isKinematic = true;

			Rigidbody rootRb = GetComponent<Rigidbody>();
			if (rootRb != null && !rootRb.isKinematic)
			{
				rootRb.constraints = RigidbodyConstraints.FreezePositionX |
				                     RigidbodyConstraints.FreezePositionZ |
				                     RigidbodyConstraints.FreezeRotation;
			}

			if (singulatorMode)
			{
				pcsC = null;
				pcsS = physicsParent.AddComponent<PCSsingulator>();
			}
			else
			{
				pcsS = null;
				pcsC = physicsParent.AddComponent<PCSConveyor>();
				pcsC.speed = speed;
			}

		}

		void ApplyTransform()
		{
			transform.position = parentTransform.position;
			transform.rotation = parentTransform.rotation;
			transform.localScale = parentTransform.scale;
			_conveyorBody.transform.localRotation = Quaternion.Euler(-conveyorSlopeAngle, 0f, 0f);
		}

		int[] GetRailingCount()
		{
			int[] railingCount = { 0, 0 };
			for (int i = 0; i < 2; i++)
			{

				bool rail = false;
				for (int j = 1; j < railingData[i].enabledStates.Count - 1; j++)
				{
					if (!railingData[i].enabledStates[j] && rail)
					{

						rail = false;
					}
					if (railingData[i].enabledStates[j] && !rail)
					{
						railingCount[i]++;
						rail = true;
					}


				}
			}

			return railingCount;
		}

		void CreateRailingParents(int[] railingCount)
		{
			for (int i = 0; i < 2; i++)
			{
				railingTempParents[i] = new GameObject[railingCount[i]];
				for (int j = 0; j < railingCount[i]; j++)
				{
					railingTempParents[i][j] = new GameObject("tempRailingParent");
					railingTempParents[i][j].transform.parent = _conveyorBody.transform;
					railingTempParents[i][j].transform.localPosition = _geometry.beltCenter;

					for (int k = 0; k < railing.prefab.transform.childCount; k++)
					{
						GameObject child = new GameObject(railing.prefab.transform.GetChild(k).name);
						child.transform.parent = railingTempParents[i][j].transform;
						child.transform.localPosition = Vector3.zero;
					}
				}
			}
		}



		public void deleteAllColliders()
		{
			foreach (Collider c in GetComponents<Collider>())
			{
				DestroyImmediate(c);
			}
		}


		[ContextMenu("Attempt Fix")]
		public void Fix()
		{
			int childCount = transform.childCount;

			for (int i = 0, index =0; i < childCount; i++)
			{
				if (transform.GetChild(index).name == "Belt" || transform.GetChild(index).name == "Railing" || transform.GetChild(index).name == "Start Cap" || transform.GetChild(index).name == "End Cap" || transform.GetChild(index).name == "Internals" || transform.GetChild(index).name == "Physics")
					DestroyImmediate(transform.GetChild(index).gameObject);
				else
					index++;
			}

			deleteAllColliders();

			CreatePCS();
		}

		[ContextMenu("Reset Style Settings")]
		public void ResetSettings()
		{
			belt.prefab = null;
			startCap.prefab = null;
			endCap.prefab = null;
			railing.prefab = null; ;
			railingStartCap.prefab = null; 
			railingEndCap.prefab = null;
			railingDoubleCap.prefab = null;
			internals.prefab = null;
			settingsImported = false;
		}


		[ContextMenu("Delete All Children and Colliders")]
		public void deleteAllChildren()
		{
			//if (editMode == EditModes.Railings)
			//	editMode = EditModes.None;

			int childCount = transform.childCount;

			for (int i = 0; i < childCount; i++)
			{
				DestroyImmediate(transform.GetChild(0).gameObject);
			}

			//Debug.Log("Children Destroyed (" + transform.childCount + "/" + childCount + " Remaining)");

			foreach (Collider c in GetComponents<Collider>())
			{
				DestroyImmediate(c);
			}
		}

		public void EditRailings()
		{
			if (editMode == EditModes.Railings)
			{
				railing.parent.SetActive(false);
				railingStartCap.parent.SetActive(false);
				railingEndCap.parent.SetActive(false);
				railingEditCollidersSideIndex = new Dictionary<Collider, int>();
				railingEditCollidersRailingIndex = new Dictionary<Collider, int>();
			}
			else
			{
				railing.parent.SetActive(true);
				railingStartCap.parent.SetActive(true);
				railingEndCap.parent.SetActive(true);

			}
		}

		public void InstantiateMaterials()
		{
			MeshRenderer beltParentRenderer = belt.parent.GetComponent<MeshRenderer>();
			MeshRenderer startCapRenderer = startCap.gameObject.GetComponent<MeshRenderer>();
			MeshRenderer endCapRenderer = endCap.gameObject.GetComponent<MeshRenderer>();
			MeshRenderer internalsRenderer = internals.parent.GetComponent<MeshRenderer>();
			Material railingMat = Instantiate(railing.parent.transform.GetChild(0).gameObject.GetComponent<MeshRenderer>().sharedMaterial);


			beltParentRenderer.sharedMaterial = Instantiate(beltParentRenderer.sharedMaterial);
			//beltParentRenderer.sharedMaterial.mainTextureScale = new Vector2(1, length);
			belt.parent.ScaleUVs(new Vector2(width,length));
			//startCap.gameObject.transform.GetChild(1).gameObject.ScaleUVs(new Vector2(width, 1));
			//endCap.gameObject.transform.GetChild(1).gameObject.ScaleUVs(new Vector2(width, 1));

			startCapRenderer.sharedMaterial = Instantiate(startCapRenderer.sharedMaterial);
			endCapRenderer.sharedMaterial = Instantiate(endCapRenderer.sharedMaterial);

			internalsRenderer.sharedMaterial = Instantiate(internalsRenderer.sharedMaterial);

			foreach(MeshRenderer r in railing.parent.GetComponentsInChildren<MeshRenderer>())
			{
				if (r.sharedMaterial.name == "RailColor")
					r.sharedMaterial = railingMat;
			}

			foreach (MeshRenderer r in railingStartCap.parent.GetComponentsInChildren<MeshRenderer>())
			{
				if (r.sharedMaterial.name == "RailColor")
					r.sharedMaterial = railingMat;
			}

			foreach (MeshRenderer r in railingEndCap.parent.GetComponentsInChildren<MeshRenderer>())
			{
				if (r.sharedMaterial.name == "RailColor")
					r.sharedMaterial = railingMat;
			}

			railingMat.color = colour;
		}

		public void SetSpeed(float v)
		{
			if (pcsC != null)
			{
				pcsC.speed = v;

				for (int i = 0; i < 3; i++)
					uvS[i].speed = v / 0.2f;

				uvS[3].speed = v / (0.18f*Mathf.PI);



				speed = v;
			}
			else
				Debug.LogWarning("Cannot set conveyor speed before conveyor has been created");
		}

		private void OnDrawGizmos()
		{
			if (editMode == EditModes.Railings)
			{
				RenderRailingGizmos();
			}
		}

		void RenderRailingGizmos()
		{
			for (int i = 0; i < 2; i++)
			{
				if (railingData[i].enabledStates != null)
				{
					Vector3 instantiatePosition = startCap.positionOffset;
					for (int j = 0; j < railingData[i].enabledStates.Count; j++)
					{
						for (int k = 0; k < railing.prefab.transform.childCount; k++)
						{
							if (railing.renderers != null && k < railing.renderers.Length && railing.renderers[k] != null)
							{

								Gizmos.color = railingData[i].enabledStates[j] ? Color.green : Color.red;

								Vector3 centre = railing.renderers[k].bounds.center;
								Vector3 offset = new Vector3((GetBeltSurfaceWidth() / 2f) + 0.1f, 0 ,0);
								centre.x *= (i == 0 ? 1 : -1);
								offset.x *= (i == 0 ? 1 : -1);
								Vector3 position = instantiatePosition + centre + offset;
								Vector3 size = railing.renderers[k].bounds.size;

								Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
							
								Gizmos.DrawWireCube(position, size);
							}
						}
						instantiatePosition += belt.positionOffset;
					}
				}
			}
		}

	}
}
