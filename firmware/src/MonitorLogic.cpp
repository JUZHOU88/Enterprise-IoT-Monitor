#include "MonitorLogic.h"
#include <cmath>
namespace monitor {
MonitorLogic::MonitorLogic(float minimum,float maximum,uint32_t doorHoldMs):
    minimum_(minimum),maximum_(maximum),doorHoldMs_(doorHoldMs) {
    // Avoid exceptions: many embedded toolchains compile with -fno-exceptions.
    if (!std::isfinite(minimum)||!std::isfinite(maximum)||minimum>=maximum) {
        minimum_=18.0f;
        maximum_=28.0f;
    }
}
void MonitorLogic::updateDoor(bool doorOpen,uint32_t nowMs) {
    if (doorOpen && !doorOpen_) doorOpenedAt_=nowMs;
    doorOpen_=doorOpen;
}
Decision MonitorLogic::evaluate(float tempC,bool sensorOk,uint32_t nowMs) const {
    bool fault=!sensorOk || !std::isfinite(tempC);
    bool temp=!fault && (tempC<minimum_ || tempC>maximum_);
    bool door=doorOpen_ && static_cast<uint32_t>(nowMs-doorOpenedAt_)>=doorHoldMs_;
    return {fault||temp||door,fault,temp,door};
}
}
