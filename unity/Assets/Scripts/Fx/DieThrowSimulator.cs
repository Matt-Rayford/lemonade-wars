using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LemonadeWars.Unity
{
    /// <summary>
    /// Throws a real rigidbody die across an invisible table in its own physics scene
    /// and records the flight, ahead of time, in a few milliseconds. The arena is the
    /// patch of floor the die camera can see: walls close behind the die once it is
    /// inside, so it bounces off the screen edges and settles somewhere in view.
    /// Which number ends up on top is the renderer's problem (it relabels the faces);
    /// hops for modifier flips retry until the wanted face lands up.
    /// </summary>
    public sealed class DieThrowSimulator
    {
        public const float StepSeconds = 1f / 60f;
        /// <summary>
        /// Die edge = 1 unit, so real gravity would be ~600. Far gentler on purpose: the
        /// roll should be watchable, and PhysX treats any contact faster than its bounce
        /// threshold (2 units/s) as a bounce — at 150 the die gained more than that
        /// EVERY step and jittered on the floor forever instead of coming to rest.
        /// </summary>
        private const float Gravity = 30f;
        private const float MaxThrowSeconds = 5f;
        private const float MaxHopSeconds = 2.5f;
        private const int ThrowAttempts = 6;
        private const int HopAttempts = 40;
        private const float WallHeight = 10f;
        private const float WallThickness = 2f;
        /// <summary>The die's half-diagonal is 0.87: this far inside, no wall can overlap it when it closes.</summary>
        private const float EntryMargin = 1.2f;
        private const float RestMargin = 0.4f;
        private const float SpawnOffset = 2.5f;
        private const float RestSpeed = 0.1f;
        private const int RestSteps = 12;

        private readonly PhysicsScene _physics;
        private readonly Scene _scene;
        private readonly Rigidbody _die;
        private readonly PhysicsMaterial _surface;
        private readonly List<BoxCollider> _walls = new List<BoxCollider>();
        private Vector2[] _arena = new Vector2[0];

        public DieThrowSimulator()
        {
            _scene = SceneManager.CreateScene("DiePhysics",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            _physics = _scene.GetPhysicsScene();
            _surface = new PhysicsMaterial("DieTable")
            {
                bounciness = 0.35f,
                dynamicFriction = 0.5f,
                staticFriction = 0.6f,
                bounceCombine = PhysicsMaterialCombine.Average,
                frictionCombine = PhysicsMaterialCombine.Average,
            };

            var floor = new GameObject("DieFloor", typeof(BoxCollider));
            var floorBox = floor.GetComponent<BoxCollider>();
            floorBox.size = new Vector3(400f, 2f, 400f);
            floorBox.center = new Vector3(0f, -1f, 0f);
            floorBox.sharedMaterial = _surface;
            SceneManager.MoveGameObjectToScene(floor, _scene);

            var dieGo = new GameObject("PhysicsDie", typeof(BoxCollider), typeof(Rigidbody));
            dieGo.GetComponent<BoxCollider>().sharedMaterial = _surface;
            _die = dieGo.GetComponent<Rigidbody>();
            _die.mass = 1f;
            _die.useGravity = false;
            _die.linearDamping = 0.1f;
            _die.angularDamping = 0.4f;
            _die.maxAngularVelocity = 60f;
            _die.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _die.interpolation = RigidbodyInterpolation.None;
            SceneManager.MoveGameObjectToScene(dieGo, _scene);
        }

        /// <summary>The visible floor as a convex quad in xz, corners in order around it.</summary>
        public void SetArena(Vector2[] cornersXz)
        {
            foreach (var wall in _walls)
            {
                Object.Destroy(wall.gameObject);
            }
            _walls.Clear();
            _arena = cornersXz;
            for (int i = 0; i < cornersXz.Length; i++)
            {
                var a = cornersXz[i];
                var b = cornersXz[(i + 1) % cornersXz.Length];
                var along = (b - a).normalized;
                var inward = InwardNormal(a, b);
                var middle = (a + b) * 0.5f - inward * (WallThickness * 0.5f);
                var wallGo = new GameObject("DieWall" + i, typeof(BoxCollider));
                wallGo.transform.position = new Vector3(middle.x, WallHeight * 0.5f, middle.y);
                wallGo.transform.rotation = Quaternion.FromToRotation(Vector3.right, new Vector3(along.x, 0f, along.y));
                var box = wallGo.GetComponent<BoxCollider>();
                box.size = new Vector3((b - a).magnitude + 2f * WallThickness, WallHeight, WallThickness);
                box.sharedMaterial = _surface;
                SceneManager.MoveGameObjectToScene(wallGo, _scene);
                _walls.Add(box);
            }
        }

        /// <summary>A fresh throw in from a random edge; the recording ends with the die at rest in view.</summary>
        public DieThrowRecording RecordThrow()
        {
            DieThrowRecording last = null;
            for (int attempt = 0; attempt < ThrowAttempts; attempt++)
            {
                last = SimulateThrow();
                if (IsSettledInView(last))
                {
                    return last;
                }
            }
            return last;
        }

        /// <summary>
        /// A short hop from where the die lies, retried until <paramref name="accepts"/>
        /// likes the resting rotation (the caller checks which number faces up).
        /// </summary>
        public DieThrowRecording RecordHop(Vector3 from, Quaternion fromRotation,
            System.Func<Quaternion, bool> accepts)
        {
            DieThrowRecording last = null;
            for (int attempt = 0; attempt < HopAttempts; attempt++)
            {
                last = SimulateHop(from, fromRotation);
                if (IsSettledInView(last) && accepts(last.FinalRotation))
                {
                    return last;
                }
            }
            return last;
        }

        private DieThrowRecording SimulateThrow()
        {
            int edge = Random.Range(0, _arena.Length);
            var a = _arena[edge];
            var b = _arena[(edge + 1) % _arena.Length];
            var inward = InwardNormal(a, b);
            var spawn = Vector2.Lerp(a, b, Random.Range(0.2f, 0.8f)) - inward * SpawnOffset;
            var heading = Rotate(inward, Random.Range(-35f, 35f)) * Random.Range(15f, 20f);
            var velocity = new Vector3(heading.x, Random.Range(-1f, 3f), heading.y);
            var start = new Vector3(spawn.x, Random.Range(2f, 4f), spawn.y);
            Launch(start, Random.rotationUniform, velocity, Random.onUnitSphere * Random.Range(10f, 22f));
            SetWalls(false);

            var recording = new DieThrowRecording(StepSeconds);
            bool wallsClosed = false;
            int restSteps = 0;
            int maxSteps = Mathf.RoundToInt(MaxThrowSeconds / StepSeconds);
            for (int step = 0; step < maxSteps; step++)
            {
                Step(recording);
                var position = _die.position;
                if (!wallsClosed && IsInside(position, EntryMargin))
                {
                    SetWalls(true);
                    wallsClosed = true;
                }
                if (wallsClosed && IsResting(ref restSteps))
                {
                    break;
                }
            }
            return recording;
        }

        private DieThrowRecording SimulateHop(Vector3 from, Quaternion fromRotation)
        {
            var sideways = Random.insideUnitCircle * 1.5f;
            var velocity = new Vector3(sideways.x, Random.Range(4.5f, 6.5f), sideways.y);
            Launch(from, fromRotation, velocity, Random.onUnitSphere * Random.Range(5f, 10f));
            SetWalls(true);

            var recording = new DieThrowRecording(StepSeconds);
            int restSteps = 0;
            int maxSteps = Mathf.RoundToInt(MaxHopSeconds / StepSeconds);
            for (int step = 0; step < maxSteps; step++)
            {
                Step(recording);
                if (IsResting(ref restSteps))
                {
                    break;
                }
            }
            return recording;
        }

        private void Launch(Vector3 position, Quaternion rotation, Vector3 velocity, Vector3 angularVelocity)
        {
            _die.transform.SetPositionAndRotation(position, rotation);
            _die.position = position;
            _die.rotation = rotation;
            _die.linearVelocity = velocity;
            _die.angularVelocity = angularVelocity;
            _die.WakeUp();
        }

        private void Step(DieThrowRecording recording)
        {
            _die.AddForce(Vector3.down * Gravity, ForceMode.Acceleration);
            _physics.Simulate(StepSeconds);
            recording.Add(_die.position, _die.rotation);
        }

        private bool IsResting(ref int restSteps)
        {
            bool still = _die.linearVelocity.magnitude < RestSpeed && _die.angularVelocity.magnitude < RestSpeed;
            restSteps = still ? restSteps + 1 : 0;
            return restSteps >= RestSteps || _die.IsSleeping();
        }

        private bool IsSettledInView(DieThrowRecording recording) =>
            recording.DurationSeconds < MaxThrowSeconds - StepSeconds &&
            IsInside(recording.FinalPosition, RestMargin) &&
            DieMesh.IsFlat(recording.FinalRotation);

        private void SetWalls(bool enabled)
        {
            foreach (var wall in _walls)
            {
                wall.enabled = enabled;
            }
        }

        /// <summary>Inside the arena quad by at least <paramref name="margin"/> on every side.</summary>
        private bool IsInside(Vector3 position, float margin)
        {
            var point = new Vector2(position.x, position.z);
            for (int i = 0; i < _arena.Length; i++)
            {
                var a = _arena[i];
                var b = _arena[(i + 1) % _arena.Length];
                if (Vector2.Dot(point - a, InwardNormal(a, b)) < margin)
                {
                    return false;
                }
            }
            return _arena.Length > 0;
        }

        private Vector2 InwardNormal(Vector2 a, Vector2 b)
        {
            var along = (b - a).normalized;
            var normal = new Vector2(-along.y, along.x);
            var centroid = Vector2.zero;
            foreach (var corner in _arena)
            {
                centroid += corner / _arena.Length;
            }
            return Vector2.Dot(centroid - a, normal) >= 0f ? normal : -normal;
        }

        private static Vector2 Rotate(Vector2 v, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float cos = Mathf.Cos(radians);
            float sin = Mathf.Sin(radians);
            return new Vector2(v.x * cos - v.y * sin, v.x * sin + v.y * cos);
        }
    }
}
