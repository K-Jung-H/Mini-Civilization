using System;
using System.Collections.Generic;
using MiniCivilization.World.Entities;
using UnityEngine;

namespace MiniCivilization.World.Authoring
{
    [DisallowMultipleComponent]
    public sealed class BuildingWayPointMarker : MonoBehaviour
    {
        [Serializable]
        public struct Connection
        {
            [SerializeField] private BuildingWayPointMarker target;
            [SerializeField] private bool oneWay;

            public BuildingWayPointMarker Target => target;
            public bool OneWay => oneWay;
        }

        [SerializeField]
        private BuildingWayPointDirection externalDirection;

        [SerializeField]
        private List<Connection> connections = new();

        public BuildingWayPointDirection ExternalDirection =>
            externalDirection;
        public IReadOnlyList<Connection> Connections => connections;
    }
}
