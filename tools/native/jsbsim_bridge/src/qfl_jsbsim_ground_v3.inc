// Per-instance bounded ground callback. No managed call or dynamic allocation
// during a contact query; the host refreshes the patch before leaving coverage.
class PreparedGround final : public JSBSim::FGGroundCallback {
public:
  qfl_jsbsim_terrain_patch_v3 patch{};
  std::array<double,1089> heights{};
  mutable bool outside = false;
  bool initializing_coordinates = false;
  bool Covers(double latitude,double longitude) const {
    return latitude>=patch.minimum_latitude_degrees && longitude>=patch.minimum_longitude_degrees &&
      latitude<=patch.minimum_latitude_degrees+(patch.rows-1)*patch.latitude_step_degrees &&
      longitude<=patch.minimum_longitude_degrees+(patch.columns-1)*patch.longitude_step_degrees;
  }
  double a=20925646.32546,b=20855486.5951;
  void SetEllipse(double major,double minor) override { a=major;b=minor; }
  double GetAGLevel(double,const JSBSim::FGLocation& location,JSBSim::FGLocation& contact,
      JSBSim::FGColumnVector3& normal,JSBSim::FGColumnVector3& velocity,JSBSim::FGColumnVector3& angular) const override {
    constexpr double rad=3.14159265358979323846/180.0;
    // Gear constructs a location from ECEF coordinates without an ellipsoid.
    // Match JSBSim's default callback: copy and establish the planet first.
    JSBSim::FGLocation geodetic=location;geodetic.SetEllipse(a,b);
    const double lat=geodetic.GetGeodLatitudeRad(),lon=geodetic.GetLongitude();
    double row=(lat/rad-patch.minimum_latitude_degrees)/patch.latitude_step_degrees;
    double col=(lon/rad-patch.minimum_longitude_degrees)/patch.longitude_step_degrees;
    if(row<0||col<0||row>patch.rows-1||col>patch.columns-1) {
      // Individual JSBSim IC setters query terrain at intermediate lat/lon.
      // Final reset coverage is checked before entering this narrow phase.
      // Runtime and RunIC queries always remain strict.
      if(initializing_coordinates) {
        row=std::max(0.0,std::min(row,static_cast<double>(patch.rows-1)));
        col=std::max(0.0,std::min(col,static_cast<double>(patch.columns-1)));
      } else {
      outside=true;
      throw std::runtime_error("Native terrain contact outside prepared patch at lat="+std::to_string(lat/rad)+", lon="+std::to_string(lon/rad)+"; simulation halted.");
      }
    }
    int i=std::min(static_cast<int>(row),patch.rows-2),j=std::min(static_cast<int>(col),patch.columns-2);
    double y=row-i,x=col-j;
    double h00=heights[i*patch.columns+j],h10=heights[i*patch.columns+j+1];
    double h01=heights[(i+1)*patch.columns+j],h11=heights[(i+1)*patch.columns+j+1];
    double h=(1-y)*((1-x)*h00+x*h10)+y*((1-x)*h01+x*h11);
    contact.SetEllipse(a,b);contact.SetPositionGeodetic(lon,lat,h/.3048);
    double e2=1-b*b/(a*a),factor=1-e2*std::sin(lat)*std::sin(lat);
    double prime=a/std::sqrt(factor),meridian=a*(1-e2)/std::pow(factor,1.5);
    double eastSlope=((1-y)*(h10-h00)+y*(h11-h01))/(patch.longitude_step_degrees*rad*prime*.3048*std::cos(lat));
    double northSlope=((1-x)*(h01-h00)+x*(h11-h10))/(patch.latitude_step_degrees*rad*meridian*.3048);
    JSBSim::FGColumnVector3 east(-std::sin(lon),std::cos(lon),0);
    JSBSim::FGColumnVector3 north(-std::sin(lat)*std::cos(lon),-std::sin(lat)*std::sin(lon),std::cos(lat));
    JSBSim::FGColumnVector3 up(std::cos(lat)*std::cos(lon),std::cos(lat)*std::sin(lon),std::sin(lat));
    normal=up-east*eastSlope-north*northSlope;normal.Normalize();
    velocity.InitMatrix();angular.InitMatrix();
    // JSBSim expects geometric altitude along ellipsoidal vertical. Normal is
    // used independently by its contact/friction solver.
    return geodetic.GetGeodAltitude()-h/.3048;
  }
};
