using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PCS
{
	[RequireComponent(typeof(Rigidbody))]
	public class PCSConveyor : MonoBehaviour
	{
		Rigidbody rb;
		public float speed;

		[Tooltip("Friction coefficient for force-based belt driving (μ). " +
				 "Overrides PhysX material friction so slanted belts work with zero-friction packages. " +
				 "Typical rubber-on-cardboard ≈ 0.5–0.8.")]
		public float frictionCoefficient = 0.7f;

		private BoxCollider bc;

		private BoxCollider FindPrimarySurfaceCollider()
		{
			BoxCollider[] colliders = GetComponents<BoxCollider>();
			BoxCollider best = null;
			float bestScore = float.NegativeInfinity;

			for (int i = 0; i < colliders.Length; i++)
			{
				BoxCollider candidate = colliders[i];
				Vector3 size = Vector3.Scale(candidate.size, transform.lossyScale);
				float score = Mathf.Abs(size.x * size.z);
				if (best == null || score > bestScore)
				{
					best = candidate;
					bestScore = score;
				}
			}

			return best;
		}

		private void Start()
		{
			rb = GetComponent<Rigidbody>();
			bc = FindPrimarySurfaceCollider();

			// Zero PhysX friction — packages are driven by explicit AddForce, not surface friction.
			// Non-zero friction here would double-drive packages (kinematic surface + explicit force)
			// and cause sticking to belt edges.
			if (bc != null)
				bc.material = new PhysicsMaterial("PCSConveyorSurface")
				{
					dynamicFriction = 0f,
					staticFriction  = 0f,
					frictionCombine = PhysicsMaterialCombine.Minimum,
					bounciness      = 0f,
					bounceCombine   = PhysicsMaterialCombine.Minimum,
				};
		}

		void FixedUpdate()
		{
			// Sweep the kinematic collider through the contact patch, then end the step
			// back at the authored position so the visual conveyor stays stationary.
			Vector3 authoredPosition = transform.position;
			Vector3 delta = transform.forward * speed * Time.fixedDeltaTime;
			transform.position = authoredPosition - delta;
			Physics.SyncTransforms();
			rb.MovePosition(authoredPosition);

			// Force assist keeps packages tracking the target belt speed on slanted belts
			// and with low-friction package colliders.
			if (bc != null)
				DrivePackages();
		}

		private void DrivePackages()
		{
			// Scan slightly above the belt surface to catch boxes resting on top.
			Vector3 worldCenter = transform.TransformPoint(bc.center + Vector3.up * 0.05f);
			Vector3 halfExtents = new Vector3(
				bc.size.x * 0.5f * transform.lossyScale.x,
				Mathf.Max(0.08f, bc.size.y * 0.5f * transform.lossyScale.y + 0.05f),
				bc.size.z * 0.5f * transform.lossyScale.z
			);

			Collider[] hits = Physics.OverlapBox(
				worldCenter, halfExtents, transform.rotation,
				~0, QueryTriggerInteraction.Ignore
			);

			Vector3 beltDir = transform.forward;

			foreach (Collider hit in hits)
			{
				if (hit.gameObject == gameObject) continue;

				Rigidbody packageRb = hit.attachedRigidbody;
				if (packageRb == null || packageRb.isKinematic) continue;

				float slip = speed - Vector3.Dot(packageRb.linearVelocity, beltDir);
				if (Mathf.Abs(slip) < 0.001f) continue;

				// Guarantee enough forward drive to converge toward belt speed within a
				// physics step, while still allowing extra grip from the friction model.
				float normalForce = packageRb.mass * Mathf.Abs(Vector3.Dot(Physics.gravity, transform.up));
				float frictionForce = frictionCoefficient * normalForce;
				float catchUpForce = packageRb.mass * Mathf.Abs(slip) / Time.fixedDeltaTime;
				float appliedForce = Mathf.Max(frictionForce, catchUpForce);

				packageRb.AddForce(beltDir * Mathf.Sign(slip) * appliedForce, ForceMode.Force);
			}
		}
	}
}
