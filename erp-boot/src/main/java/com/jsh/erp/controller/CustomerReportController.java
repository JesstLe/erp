package com.jsh.erp.controller;

import com.jsh.erp.base.BaseController;
import com.jsh.erp.base.TableDataInfo;
import com.jsh.erp.service.customer.CustomerReportService;
import io.swagger.annotations.Api;
import io.swagger.annotations.ApiOperation;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;

import javax.annotation.Resource;
import javax.servlet.http.HttpServletRequest;
import java.util.List;
import java.util.Map;

@RestController
@RequestMapping(value = "/customer/report")
@Api(tags = {"顾客报表"})
public class CustomerReportController extends BaseController {
    @Resource
    private CustomerReportService customerReportService;

    @GetMapping(value = "/consumption")
    @ApiOperation(value = "顾客报表-消费明细")
    public TableDataInfo consumption(@RequestParam(value = "start", required = false) String start,
                                     @RequestParam(value = "end", required = false) String end,
                                     @RequestParam(value = "depotId", required = false) Long depotId,
                                     @RequestParam(value = "memberKey", required = false) String memberKey,
                                     HttpServletRequest request) throws Exception {
        startPage();
        List<Map<String, Object>> list = customerReportService.listConsumption(start, end, depotId, memberKey);
        return getDataTable(list);
    }

    @GetMapping(value = "/credit")
    @ApiOperation(value = "顾客报表-挂账明细")
    public TableDataInfo credit(@RequestParam(value = "start", required = false) String start,
                                @RequestParam(value = "end", required = false) String end,
                                @RequestParam(value = "depotId", required = false) Long depotId,
                                @RequestParam(value = "memberKey", required = false) String memberKey,
                                @RequestParam(value = "status", required = false) String status,
                                HttpServletRequest request) throws Exception {
        startPage();
        List<Map<String, Object>> list = customerReportService.listCreditBill(start, end, depotId, memberKey, status);
        return getDataTable(list);
    }
}

