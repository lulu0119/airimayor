"""Score a street-form vector by how isolated it is among measured urban areas.

The six indicators are standardized on Boeing's global table. Rarity is the
share of urban areas whose nearest other area is closer than this vector is
to its nearest urban area. 0 sits in a crowd. 1 is more isolated than every
measured urban area.
"""

from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.neighbors import NearestNeighbors
from sklearn.preprocessing import StandardScaler

DATA = Path(__file__).with_name("data") / "indicators.csv"

FEATURES = (
    "circuity",
    "prop_deadend",
    "intersect_density_km",
    "prop_4way",
    "length_mean",
    "orientation_entropy",
)


def load_areas(path: Path = DATA) -> pd.DataFrame:
    frame = pd.read_csv(path)
    frame["intersect_density_km"] = frame["intersect_count_clean_topo"] / frame["area_km2"]
    frame["city"] = frame["core_city"].fillna("").str.replace("_", " ", regex=False)
    return frame.dropna(subset=list(FEATURES)).reset_index(drop=True)


def fit_cloud(areas: pd.DataFrame) -> tuple[StandardScaler, NearestNeighbors, np.ndarray]:
    scaler = StandardScaler().fit(areas[list(FEATURES)])
    cloud = scaler.transform(areas[list(FEATURES)])
    neighbors = NearestNeighbors(n_neighbors=2).fit(cloud)
    nearest = neighbors.kneighbors(cloud)[0][:, 1]
    return scaler, neighbors, nearest


def score(values: dict, areas: pd.DataFrame, scaler: StandardScaler, neighbors: NearestNeighbors, nearest: np.ndarray) -> dict:
    row = pd.DataFrame([{name: values[name] for name in FEATURES}])
    point = scaler.transform(row)
    distance, index = neighbors.kneighbors(point, n_neighbors=1)
    distance = float(distance[0, 0])
    neighbor = areas.iloc[int(index[0, 0])]
    gap = point[0] - scaler.transform(neighbor[list(FEATURES)].to_frame().T)[0]
    main = FEATURES[int(np.argmax(np.abs(gap)))]
    return {
        "rarity": float(np.mean(nearest < distance)),
        "distance": distance,
        "nearest_city": neighbor["city"],
        "nearest_country": neighbor["country"],
        "pulled_by": main,
    }


def main() -> None:
    areas = load_areas()
    scaler, neighbors, nearest = fit_cloud(areas)

    def show(label: str, values: dict) -> None:
        result = score(values, areas, scaler, neighbors, nearest)
        print(
            f"{label}: rarity {result['rarity']:.3f}, distance {result['distance']:.3f}, "
            f"nearest {result['nearest_city']} ({result['nearest_country']}), pulled by {result['pulled_by']}"
        )

    beijing = areas.loc[areas["city"].eq("beijing") & areas["country_iso"].eq("CHN")].iloc[0]
    show("beijing as measured", {name: float(beijing[name]) for name in FEATURES})
    point = scaler.transform(beijing[list(FEATURES)].to_frame().T)
    distance = float(neighbors.kneighbors(point, n_neighbors=2)[0][0, 1])
    print(f"beijing among the others: rarity {float(np.mean(nearest < distance)):.3f}, distance {distance:.3f}")

    grid = {name: float(beijing[name]) for name in FEATURES}
    grid["orientation_entropy"] = float(areas["orientation_entropy"].min())
    grid["prop_4way"] = float(areas["prop_4way"].max())
    grid["prop_deadend"] = float(areas["prop_deadend"].min())
    show("beijing with an extreme grid", grid)


if __name__ == "__main__":
    main()
