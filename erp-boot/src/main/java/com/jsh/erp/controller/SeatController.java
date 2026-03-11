package com.jsh.erp.controller;

import com.alibaba.fastjson.JSONObject;
import com.jsh.erp.base.BaseController;
import com.jsh.erp.datasource.entities.Seat;
import com.jsh.erp.service.SeatService;
import com.jsh.erp.utils.BaseResponseInfo;
import com.jsh.erp.utils.Constants;
import com.jsh.erp.utils.ErpInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.*;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

import static com.jsh.erp.utils.ResponseJsonUtil.returnJson;

@RestController
@RequestMapping(value = "/seat")
@Api(tags = {"座席管理"})
public class SeatController extends BaseController {

    @Resource
    private SeatService seatService;

    @GetMapping(value = "/list")
    @ApiOperation(value = "获取座席列表")
    public BaseResponseInfo getList(@RequestParam(value = Constants.SEARCH, required = false) String search,
                                    @RequestParam("currentPage") Integer currentPage,
                                    @RequestParam("pageSize") Integer pageSize,
                                    HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        Map<String, Object> map = new HashMap<>();
        try {
            String name = null;
            Long depotId = null;
            if(search != null) {
                JSONObject obj = JSONObject.parseObject(search);
                name = obj.getString("name");
                if(obj.containsKey("depotId")) {
                    depotId = obj.getLong("depotId");
                }
            }
            List<Seat> list = seatService.select(name, depotId, (currentPage - 1) * pageSize, pageSize);
            Long total = seatService.countSeat(name, depotId);
            map.put("rows", list);
            map.put("total", total);
            res.code = 200;
            res.data = map;
        } catch (Exception e) {
            e.printStackTrace();
            res.code = 500;
            res.data = "获取数据失败";
        }
        return res;
    }

    @PostMapping(value = "/add")
    @ApiOperation(value = "新增座席")
    public String add(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        Map<String, Object> objectMap = new HashMap<>();
        int insert = seatService.insertSeat(obj, request);
        if(insert > 0) {
            return returnJson(objectMap, ErpInfo.OK.name, ErpInfo.OK.code);
        } else {
            return returnJson(objectMap, ErpInfo.ERROR.name, ErpInfo.ERROR.code);
        }
    }

    @PutMapping(value = "/update")
    @ApiOperation(value = "修改座席")
    public String update(@RequestBody JSONObject obj, HttpServletRequest request) throws Exception {
        Map<String, Object> objectMap = new HashMap<>();
        int update = seatService.updateSeat(obj, request);
        if(update > 0) {
            return returnJson(objectMap, ErpInfo.OK.name, ErpInfo.OK.code);
        } else {
            return returnJson(objectMap, ErpInfo.ERROR.name, ErpInfo.ERROR.code);
        }
    }

    @DeleteMapping(value = "/delete")
    @ApiOperation(value = "删除座席")
    public String delete(@RequestParam("id") Long id, HttpServletRequest request) throws Exception {
        Map<String, Object> objectMap = new HashMap<>();
        int delete = seatService.deleteSeat(id, request);
        if(delete > 0) {
            return returnJson(objectMap, ErpInfo.OK.name, ErpInfo.OK.code);
        } else {
            return returnJson(objectMap, ErpInfo.ERROR.name, ErpInfo.ERROR.code);
        }
    }

    @DeleteMapping(value = "/deleteBatch")
    @ApiOperation(value = "批量删除座席")
    public String deleteBatch(@RequestParam("ids") String ids, HttpServletRequest request) throws Exception {
        Map<String, Object> objectMap = new HashMap<>();
        int delete = seatService.batchDeleteSeat(ids, request);
        if(delete > 0) {
            return returnJson(objectMap, ErpInfo.OK.name, ErpInfo.OK.code);
        } else {
            return returnJson(objectMap, ErpInfo.ERROR.name, ErpInfo.ERROR.code);
        }
    }
}
