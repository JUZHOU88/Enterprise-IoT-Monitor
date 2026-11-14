#pragma once
#include <cstdint>

namespace monitor {
struct Reading { float temperatureC; bool sensorOk; bool doorOpen; };
struct Decision { bool alert; bool sensorFault; bool temperatureAlarm; bool doorAlarm; };
class MonitorLogic {
public:
    MonitorLogic(float minimum=18.0f,float maximum=28.0f,uint32_t doorHoldMs=3000);
    void updateDoor(bool doorOpen, uint32_t nowMs);
    Decision evaluate(float tempC,bool sensorOk,uint32_t nowMs) const;
private:
    float minimum_, maximum_;
    uint32_t doorHoldMs_;
    uint32_t doorOpenedAt_ = 0;
    bool doorOpen_ = false;
};
}
