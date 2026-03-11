package com.jsh.erp.controller;

import com.jsh.erp.base.BaseController;
import com.jsh.erp.service.customer.CustomerDashboardService;
import com.jsh.erp.utils.BaseResponseInfo;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping(value = "/customer/dashboard")
@Api(tags = {"顾客看板"})
public class CustomerDashboardController extends BaseController {
    @Resource
    private CustomerDashboardService customerDashboardService;

    @GetMapping(value = "/summary")
    @ApiOperation(value = "顾客看板-汇总指标")
    public BaseResponseInfo summary(@RequestParam(value = "range", required = false) String range,
                                    @RequestParam(value = "start", required = false) String start,
                                    @RequestParam(value = "end", required = false) String end,
                                    @RequestParam(value = "depotId", required = false) Long depotId,
                                    HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            res.code = 200;
            res.data = customerDashboardService.summary(range, start, end, depotId);
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @GetMapping(value = "/consumeTypeRatio")
    @ApiOperation(value = "顾客看板-消费类型占比")
    public BaseResponseInfo consumeTypeRatio(@RequestParam(value = "range", required = false) String range,
                                             @RequestParam(value = "start", required = false) String start,
                                             @RequestParam(value = "end", required = false) String end,
                                             @RequestParam(value = "depotId", required = false) Long depotId,
                                             HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            res.code = 200;
            res.data = customerDashboardService.consumeTypeRatio(range, start, end, depotId);
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @GetMapping(value = "/revenueTrend")
    @ApiOperation(value = "顾客看板-营业额趋势")
    public BaseResponseInfo revenueTrend(@RequestParam(value = "range", required = false) String range,
                                         @RequestParam(value = "start", required = false) String start,
                                         @RequestParam(value = "end", required = false) String end,
                                         @RequestParam(value = "depotId", required = false) Long depotId,
                                         HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            List<Map<String, Object>> list = customerDashboardService.revenueTrend(range, start, end, depotId);
            Map<String, Object> map = new HashMap<>();
            map.put("rows", list);
            map.put("total", list == null ? 0 : list.size());
            res.code = 200;
            res.data = map;
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }

    @GetMapping(value = "/recentMembers")
    @ApiOperation(value = "顾客看板-最近新增会员")
    public BaseResponseInfo recentMembers(@RequestParam(value = "limit", required = false) Integer limit,
                                          HttpServletRequest request) throws Exception {
        BaseResponseInfo res = new BaseResponseInfo();
        try {
            res.code = 200;
            res.data = customerDashboardService.recentMembers(limit);
        } catch (Exception e) {
            res.code = 500;
            res.data = "获取失败";
        }
        return res;
    }
}

