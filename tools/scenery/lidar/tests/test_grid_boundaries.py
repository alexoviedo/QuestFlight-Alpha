from __future__ import annotations

import sys
from dataclasses import replace
from pathlib import Path

import numpy as np

LIDAR_DIR=Path(__file__).resolve().parents[1]
sys.path.insert(0,str(LIDAR_DIR))
from common import compute_grid_contract,load_config


def test_real_config_edges_corners_and_neighboring_tiles():
    contract=compute_grid_contract(load_config(LIDAR_DIR/'config.json'))
    lo_x,hi_x,lo_y,hi_y=contract.min_easting,contract.max_easting,contract.min_northing,contract.max_northing
    mid_x,mid_y=(lo_x+hi_x)/2,(lo_y+hi_y)/2
    x=np.array([lo_x,hi_x,mid_x,mid_x,lo_x,hi_x,lo_x,hi_x,
                np.nextafter(lo_x,-np.inf),np.nextafter(lo_x,np.inf),np.nextafter(hi_x,-np.inf),np.nextafter(hi_x,np.inf),mid_x,mid_x,mid_x,mid_x])
    y=np.array([mid_y,mid_y,lo_y,hi_y,lo_y,lo_y,hi_y,hi_y,mid_y,mid_y,mid_y,mid_y,
                np.nextafter(lo_y,-np.inf),np.nextafter(lo_y,np.inf),np.nextafter(hi_y,-np.inf),np.nextafter(hi_y,np.inf)])
    inside,rows,columns=contract.flat_indices(x,y)
    assert inside.tolist()==[True,False,False,True,False,False,True,False,False,True,True,False,False,True,True,False]
    assert np.all((rows>=0)&(rows<contract.height)&(columns>=0)&(columns<contract.width))
    accumulation=np.zeros(contract.height*contract.width)
    np.add.at(accumulation,rows.astype(np.int64)*contract.width+columns,1)
    assert accumulation.sum()==inside.sum()
    north=replace(contract,min_northing=hi_y,max_northing=hi_y+(hi_y-lo_y))
    east=replace(contract,min_easting=hi_x,max_easting=hi_x+(hi_x-lo_x))
    # Shared north edge belongs to the southern tile; east edge to eastern tile.
    assert contract.flat_indices(np.array([mid_x]),np.array([hi_y]))[0][0]
    assert not north.flat_indices(np.array([mid_x]),np.array([hi_y]))[0][0]
    assert not contract.flat_indices(np.array([hi_x]),np.array([mid_y]))[0][0]
    assert east.flat_indices(np.array([hi_x]),np.array([mid_y]))[0][0]
