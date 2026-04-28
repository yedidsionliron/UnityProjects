using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Reflection;

namespace PCS
{
	public static class PCSUtils
	{ 

		public static void CombineChildMeshes(this GameObject parentGameObject, Material material)
		{
			MeshFilter[] meshFilters = parentGameObject.GetComponentsInChildren<MeshFilter>();
			CombineInstance[] combine = new CombineInstance[meshFilters.Length];
			Matrix4x4 parentWorldToLocal = parentGameObject.transform.worldToLocalMatrix;
			int i = 0;
			while (i < meshFilters.Length)
			{
				combine[i].mesh = meshFilters[i].sharedMesh;
				combine[i].transform = parentWorldToLocal * meshFilters[i].transform.localToWorldMatrix;
				GameObject.DestroyImmediate(meshFilters[i].gameObject);
				i++;
			}
			MeshFilter parentMesh = parentGameObject.AddComponent<MeshFilter>();
			parentMesh.sharedMesh = new Mesh();
			parentMesh.sharedMesh.name = "Merged Mesh";
			parentMesh.sharedMesh.CombineMeshes(combine, true, true);
			MeshRenderer parentRenderer = parentGameObject.AddComponent<MeshRenderer>();
			parentRenderer.sharedMaterial = material;
			parentGameObject.SetActive(true);
		}

		public static Collider AddDuplicateCollider(this GameObject g, Collider collider)
		{
			Collider c = null;

			if (collider.GetType() == typeof(BoxCollider))
			{
				BoxCollider source = (BoxCollider)collider;
				BoxCollider box = g.AddComponent<BoxCollider>();
				Vector3 worldCenter = collider.transform.TransformPoint(source.center);
				box.center = g.transform.InverseTransformPoint(worldCenter);

				Vector3 sourceLossyScale = collider.transform.lossyScale;
				Vector3 targetLossyScale = g.transform.lossyScale;
				Vector3 worldSize = Vector3.Scale(source.size, new Vector3(
					Mathf.Abs(sourceLossyScale.x),
					Mathf.Abs(sourceLossyScale.y),
					Mathf.Abs(sourceLossyScale.z)));
				box.size = new Vector3(
					targetLossyScale.x != 0f ? worldSize.x / Mathf.Abs(targetLossyScale.x) : worldSize.x,
					targetLossyScale.y != 0f ? worldSize.y / Mathf.Abs(targetLossyScale.y) : worldSize.y,
					targetLossyScale.z != 0f ? worldSize.z / Mathf.Abs(targetLossyScale.z) : worldSize.z);
				c = box;
			}
			else if (collider.GetType() == typeof(MeshCollider))
			{
				c = g.AddComponent<MeshCollider>();
				((MeshCollider)c).sharedMesh = ((MeshCollider)collider).sharedMesh;
				((MeshCollider)c).convex = ((MeshCollider)collider).convex;
			}

			return c;
		}
	
		public static void FixScale(this GameObject g, Renderer[] childRenderers)
		{
			int childCount = g.transform.childCount;
			GameObject[] temp = new GameObject[childCount];

			for (int i = 0; i < childCount; i++)
			{
				temp[i] = new GameObject(g.transform.GetChild(0).name);
				g.transform.GetChild(0).parent = temp[i].transform;
				temp[i].CombineChildMeshes(childRenderers[i].sharedMaterial);
			}

			g.transform.localScale = Vector3.one;

			for (int i = 0; i < childCount; i++)
			{
				temp[i].transform.parent = g.transform;
			}
		}

		public static void ScaleUVs(this GameObject g, Vector2 scale)
		{
			Mesh m = g.GetComponent<MeshFilter>().sharedMesh;
			Vector2[] UVs = m.uv;

			for(int i = 0; i < UVs.Length; i++)
			{
				UVs[i] = Vector2.Scale(UVs[i], scale);
			}

			m.uv = UVs;
		}
		
		public static Vector3 GetBeltTopCenter(Vector3 startCapOffset, Vector3 beltOffset, int length)
		{
			return startCapOffset + length * beltOffset * 0.5f; 
		}

		public static Vector3 GetStartCapTopEdge(Vector3 startCapOffset)
		{
			return startCapOffset;
		}

		public static Vector3 GetEndCapTopEdge(Vector3 startCapOffset, Vector3 beltOffset, int length)
		{
			return startCapOffset + length * beltOffset;
		}

		public static float GetBeltLength(GameObject beltPrefab, int length)
		{
			float beltPrefabWidth = beltPrefab.GetComponent<Renderer>().bounds.size.z;
			return length * beltPrefabWidth;
		}

		public static float GetBeltLength(GameObject beltPrefab, int length, GameObject startCapPrefab, GameObject endCapPrefab)
		{
			float beltPrefabWidth = beltPrefab.GetComponent<Renderer>().bounds.size.z;
			float startCapPrefabWidth = startCapPrefab.GetComponent<Renderer>().bounds.size.z;
			float endCapPrefabWidth = endCapPrefab.GetComponent<Renderer>().bounds.size.z;

			return length * beltPrefabWidth + startCapPrefabWidth + endCapPrefabWidth;
		}

	}
}
